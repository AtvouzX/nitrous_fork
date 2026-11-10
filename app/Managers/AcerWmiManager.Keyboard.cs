using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrous.Enums;
using Nitrous.Helpers;

namespace Nitrous.Managers;

public static partial class AcerWmiManager
{
    private static KeyboardBacklightLevel? _backlightLevelForTesting;
    private static bool? _backlightTimeoutForTesting;

    internal static void SetBacklightLevelForTesting(KeyboardBacklightLevel? level)
    {
        _backlightLevelForTesting = level;
    }

    internal static void SetBacklightTimeoutForTesting(bool? timeout)
    {
        _backlightTimeoutForTesting = timeout;
    }

    private static int? _cachedBkHotkeyNumber;
    private static bool? _cachedIsSingleColorKeyboard;

    private static int GetBkHotkeyNumber()
    {
        if (_cachedBkHotkeyNumber.HasValue) return _cachedBkHotkeyNumber.Value;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OEM\AcerAgentService");
            if (key?.GetValue("BK_Hotkey_Number") is int val && val > 0)
            {
                _cachedBkHotkeyNumber = val;
                return val;
            }
        }
        catch { }
        _cachedBkHotkeyNumber = KeyboardBacklightCodec.DefaultBkHotkeyNumber;
        return _cachedBkHotkeyNumber.Value;
    }

    private static bool IsSingleColorKeyboard()
    {
        if (_cachedIsSingleColorKeyboard.HasValue) return _cachedIsSingleColorKeyboard.Value;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OEM\AcerAgentService\LightSetting");
            if (key?.GetValue("KeyBoardColor") is int color)
            {
                _cachedIsSingleColorKeyboard = color <= 1;
                return _cachedIsSingleColorKeyboard.Value;
            }
        }
        catch { }
        _cachedIsSingleColorKeyboard = true;
        return true;
    }

    public static async Task ApplyPhysicalBacklightLevelAsync(KeyboardBacklightLevel level)
    {
        await Task.Run(() =>
        {
            bool isSingleColor = IsSingleColorKeyboard();

            // 1. For Single-Color EC keyboards, AcerLightingService (RVA 0xB200..0xB265) first enables/disables LED behavior.
            // SetGamingLED takes a 16-byte buffer (scalar ulong throws InvalidCastException).
            if (isSingleColor)
            {
                byte[] ledBuf = KeyboardBacklightCodec.EncodeSingleColorGamingLedPayload(level);
                InvokeWmiBufferSync("AcerGamingFunction", "SetGamingLED", ledBuf);
            }

            // 2. Apply brightness payload via SetGamingKBBacklight (with brightness at payload[2] and marker 0x03/0x03)
            byte[] payload = KeyboardBacklightCodec.EncodeBacklightPayload(level, isSingleColor: isSingleColor);
            InvokeWmiBufferSync("AcerGamingFunction", "SetGamingKBBacklight", payload);

            // 3. Synchronize APGeAction.SetFunction (used by AcerAgentService when BK_Hotkey_Number is present)
            bool timeoutEnabled = GetBacklightTimeout();
            int hotkeyNum = GetBkHotkeyNumber();
            ulong apGePayload = KeyboardBacklightCodec.EncodeApGeActionSetBacklight(level, timeoutEnabled, hotkeyNum);
            InvokeApGeActionSync("SetFunction", apGePayload);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies four-zone static colors over AcerGamingFunction WMI (NitroSense V3 path used on
    /// WMI-only RGB keyboards such as AN517-42, where no ENE HID device exists).
    /// Enables all zones first, then writes one SetGamingRgbKb payload per zone.
    /// </summary>
    public static bool SetKeyboardZoneColors(IReadOnlyList<KeyboardColor> colors)
    {
        bool sent = InvokeWmiSync(
            "AcerGamingFunction", "SetGamingLEDBehavior", KeyboardZoneCodec.BuildWmiZoneEnablePayload());

        for (int zone = 0; zone < KeyboardZoneCodec.ZoneCount; zone++)
        {
            var color = zone < colors.Count ? colors[zone] : KeyboardColor.White;
            sent |= InvokeWmiSync(
                "AcerGamingFunction", "SetGamingRgbKb", KeyboardZoneCodec.BuildWmiZoneColorPayload(zone, color));
        }

        return sent;
    }

    public static async Task SetBacklightLevelAsync(KeyboardBacklightLevel level)
    {
        LogManager.Info($"Setting keyboard backlight level to {level}", "AcerWmi");
        SettingsManager.Save("KeyboardBacklightLevel", (int)level);
        await ApplyPhysicalBacklightLevelAsync(level).ConfigureAwait(false);
    }

    public static KeyboardBacklightLevel? GetBacklightLevel()
    {
        if (_backlightLevelForTesting.HasValue) return _backlightLevelForTesting.Value;

        int saved = SettingsManager.GetInt("KeyboardBacklightLevel", (int)KeyboardBacklightLevel.Level2);
        return Enum.IsDefined(typeof(KeyboardBacklightLevel), saved)
            ? (KeyboardBacklightLevel)saved
            : KeyboardBacklightLevel.Level2;
    }

    public static async Task SetBacklightTimeoutAsync(bool enabled)
    {
        LogManager.Info($"Setting keyboard backlight timeout: {(enabled ? "30s" : "Always On")}", "AcerWmi");
        SettingsManager.Save("KeyboardBacklightTimeout", enabled);

        await Task.Run(() =>
        {
            // Hardware APGeAction.SetFunction carries the 30s timeout + active brightness
            // (AcerAgentService RVA 0x3E062; NitroSense V3 Backlightoff_Checkbox_* does the same).
            // Never add AcerGamingFunction.SetGamingProfile here: the vendor timeout path does not
            // touch it, and on AN517-42 the "disabled" payload 0x00000002 kills the Windows key
            // (issue #4) while 0x01000002 restores it.
            var currentLevel = GetBacklightLevel() ?? KeyboardBacklightLevel.Level2;
            int hotkeyNum = GetBkHotkeyNumber();
            ulong apGePayload = KeyboardBacklightCodec.EncodeApGeActionSetBacklight(currentLevel, enabled, hotkeyNum);
            InvokeApGeActionSync("SetFunction", apGePayload);

            // Optional HID report fallback
            byte[] report = KeyboardBacklightCodec.EncodeTimeoutReport(enabled);
            SendKeyboardHidReport(report);
        }).ConfigureAwait(false);
    }

    public static bool GetBacklightTimeout()
    {
        if (_backlightTimeoutForTesting.HasValue) return _backlightTimeoutForTesting.Value;
        return SettingsManager.Get("KeyboardBacklightTimeout", true);
    }

    internal const string BacklightOffPendingKey = "KeyboardBacklightOffPending";
    internal const string BacklightPreviousLevelKey = "KeyboardBacklightPreviousLevel";

    public static bool IsBacklightOffPending()
    {
        return SettingsManager.Get(BacklightOffPendingKey, false);
    }

    internal static void SetBacklightOffPending(bool value)
    {
        SettingsManager.Save(BacklightOffPendingKey, value);
    }

    internal static void SetBacklightOffPendingForTesting(bool? value)
    {
        if (value.HasValue) SettingsManager.Save(BacklightOffPendingKey, value.Value);
        else SettingsManager.Delete(BacklightOffPendingKey);
    }

    /// <summary>
    /// Arms the EC-executed Off: enables the 30s timeout first, then writes the Off state.
    /// The light stays on until the EC's own 30s idle counter expires; see
    /// <see cref="KeyboardBacklightWatchdog"/> for the pending -&gt; confirmed transition.
    /// Remembers the pre-Off level so unchecking the timeout can restore it.
    /// </summary>
    public static async Task RequestBacklightOffAsync()
    {
        LogManager.Info("Arming keyboard backlight Off (EC executes after 30s idle)", "AcerWmi");
        var current = GetBacklightLevel();
        if (current.HasValue && current.Value != KeyboardBacklightLevel.Off)
        {
            SettingsManager.Save(BacklightPreviousLevelKey, (int)current.Value);
        }
        await SetBacklightTimeoutAsync(true).ConfigureAwait(false);
        await SetBacklightLevelAsync(KeyboardBacklightLevel.Off).ConfigureAwait(false);
        SettingsManager.Save(BacklightOffPendingKey, true);
    }

    /// <summary>
    /// Cancels an armed or confirmed Off: clears pending, disables the timeout,
    /// and restores the pre-Off level (defaults to Level2).
    /// </summary>
    public static async Task CancelBacklightOffAsync()
    {
        LogManager.Info("Cancelling keyboard backlight Off, restoring previous level", "AcerWmi");
        SetBacklightOffPending(false);
        await SetBacklightTimeoutAsync(false).ConfigureAwait(false);
        int previous = SettingsManager.GetInt(BacklightPreviousLevelKey, (int)KeyboardBacklightLevel.Level2);
        var level = Enum.IsDefined(typeof(KeyboardBacklightLevel), previous)
            ? (KeyboardBacklightLevel)previous
            : KeyboardBacklightLevel.Level2;
        await SetBacklightLevelAsync(level).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a hardware Fn+F11 press observed via <c>APGeEvent</c>: the EC has already
    /// stepped the hardware, so Nitrous only advances its tracked level (off -&gt; dim -&gt;
    /// bright -&gt; off) and drops any armed-Off pending state. No WMI write here — the
    /// EC owns the new state and a redundant latched write is unnecessary.
    /// </summary>
    public static void OnHardwareBacklightHotkey()
    {
        var current = GetBacklightLevel() ?? KeyboardBacklightLevel.Level2;
        var next = Helpers.BacklightHotkeyWatcher.AdvanceCycleLevel(current);
        LogManager.Info($"Hardware backlight hotkey: tracking {current} -> {next}", "AcerWmi");
        SettingsManager.Save(BacklightOffPendingKey, false);
        SettingsManager.Save("KeyboardBacklightLevel", (int)next);
    }

    private static void SendKeyboardHidReport(byte[] report)
    {
        // Safe dispatch point for optional HID report (e.g. USB keyboard)
        LogManager.Debug($"Dispatching HID report: {BitConverter.ToString(report)}", "AcerWmi");
    }
}
