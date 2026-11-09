using System.Management;
using System;
using System.Threading.Tasks;
using Nitrous.Enums;
using Nitrous.Helpers;

namespace Nitrous.Managers;

public static partial class AcerWmiManager
{
    public readonly record struct AcerCapabilities(bool Supported, bool TurboSupported, PowerProfile? ActiveMode);
    public readonly record struct UsbChargingState(bool On, int? Floor);
    internal delegate ulong? GamingProfileReaderDelegate();
    internal delegate ulong? UsbChargingReaderDelegate();
    internal delegate (bool OnAc, int BatteryPercent) PowerStateReaderDelegate();

    private static PowerStateReaderDelegate? _powerStateReader;

    internal static void SetPowerStateReaderForTesting(PowerStateReaderDelegate? testReader)
    {
        _powerStateReader = testReader;
    }

    private static GamingProfileReaderDelegate? _gamingProfileReader;
    private static UsbChargingReaderDelegate? _usbChargingReader;

    internal static void SetGamingProfileReaderForTesting(GamingProfileReaderDelegate? testReader)
    {
        lock (_wmiLock)
        {
            _gamingProfileReader = testReader;
        }
    }

    internal static void SetUsbChargingReaderForTesting(UsbChargingReaderDelegate? testReader)
    {
        lock (_wmiLock)
        {
            _usbChargingReader = testReader;
        }
    }


    public static bool IsHardwareSupported()
    {
        return GetGamingFunctionInstance() != null || AcerCapabilitiesProvider.Current.CanSetPowerMode;
    }

    internal delegate PowerProfile? ActivePowerModeReaderDelegate();
    private static ActivePowerModeReaderDelegate? _activePowerModeReader;

    internal static void SetActivePowerModeReaderForTesting(ActivePowerModeReaderDelegate? testReader)
    {
        _activePowerModeReader = testReader;
    }

    private static bool? _isTurboSupportedCached;
    private static System.Collections.Generic.IReadOnlyList<PowerProfile>? _supportedProfilesOverride;

    internal static void SetSupportedProfilesForTesting(System.Collections.Generic.IReadOnlyList<PowerProfile>? profiles)
    {
        _supportedProfilesOverride = profiles;
        _isTurboSupportedCached = profiles?.Contains(PowerProfile.Turbo);
    }

    /// <summary>
    /// Single source of truth for hardware-supported power profiles.
    /// Uses EC HID, register 0x0A, or test override.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<PowerProfile> GetSupportedProfiles()
    {
        if (_supportedProfilesOverride != null) return _supportedProfilesOverride;
        if (_isTurboSupportedCached.HasValue && !_isTurboSupportedCached.Value)
        {
            return System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Where(
                    AcerCapabilitiesProvider.Current.SupportedProfiles,
                    p => p != PowerProfile.Turbo));
        }
        return AcerCapabilitiesProvider.Current.SupportedProfiles;
    }

    /// <summary>Thin wrapper over <see cref="GetSupportedProfiles"/>.</summary>
    public static bool IsTurboModeSupported()
    {
        if (_isTurboSupportedCached.HasValue) return _isTurboSupportedCached.Value;
        return System.Linq.Enumerable.Contains(GetSupportedProfiles(), PowerProfile.Turbo);
    }

    /// <summary>Thin wrapper over <see cref="GetSupportedProfiles"/>.</summary>
    public static bool IsEcoModeSupported()
    {
        return System.Linq.Enumerable.Contains(GetSupportedProfiles(), PowerProfile.Eco);
    }

    /// <summary>True when Eco mode may be selected right now (supported and on battery).</summary>
    public static bool IsEcoModeAvailable()
    {
        var (onAc, _) = ReadPowerState();
        return !onAc && IsEcoModeSupported();
    }

    internal static void SetTurboSupportedForTesting(bool? value)
    {
        _isTurboSupportedCached = value;
    }

    public static ulong EncodePowerMode(PowerProfile profile) => AcerWmiProtocol.PowerModePayload(profile);

    public static PowerProfile DecodePowerMode(ulong rawOutput) => (PowerProfile)((rawOutput >> 8) & 0xFF);

    private static ulong? ReadMiscSetting(uint register)
    {
        MiscSettingReaderDelegate? reader;
        lock (_wmiLock)
        {
            reader = _miscSettingReader;
        }
        if (reader != null) return reader(register);

        try
        {
            var instance = GetGamingFunctionInstance();
            if (instance == null) return null;
            lock (_wmiLock)
            {
                using var inParams = instance.GetMethodParameters(AcerWmiProtocol.GetGamingMiscSetting);
                inParams["gmInput"] = register;
                using var outParams = instance.InvokeMethod(AcerWmiProtocol.GetGamingMiscSetting, inParams, null);
                object? rawValue = outParams?.Properties["gmOutput"]?.Value ?? outParams?.Properties["outValue"]?.Value;
                return rawValue != null ? Convert.ToUInt64(rawValue) : null;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"ReadMiscSetting 0x{register:X} failed: {ex.Message}", "AcerWmi");
            return null;
        }
    }

    public static AcerCapabilities GetCapabilities()
    {
        try
        {
            MiscSettingReaderDelegate? reader;
            lock (_wmiLock)
            {
                reader = _miscSettingReader;
            }
            if (reader != null) return GetCapabilitiesFromReader(reader);

            ulong? supportedRaw = ReadMiscSetting(0x0A);
            if (supportedRaw == null) return new AcerCapabilities(false, false, null);

            int supportedBitmap = (int)((supportedRaw.Value >> 8) & 0xFF);
            bool turboSupported = (supportedBitmap & (1 << 5)) != 0;

            ulong? activeRaw = ReadMiscSetting(0x0B);
            PowerProfile? activeMode = activeRaw.HasValue ? DecodePowerMode(activeRaw.Value) : null;

            return new AcerCapabilities(true, turboSupported, activeMode);
        }
        catch (Exception ex)
        {
            LogManager.Debug($"GetCapabilities failed: {ex.Message}", "AcerWmi");
        }

        return new AcerCapabilities(false, false, null);
    }

    internal static AcerCapabilities GetCapabilitiesFromReader(MiscSettingReaderDelegate reader)
    {
        ulong? supportedRaw = reader(0x0A);
        if (supportedRaw == null) return new AcerCapabilities(false, false, null);
        int supportedBitmap = (int)((supportedRaw.Value >> 8) & 0xFF);
        bool turboSupported = (supportedBitmap & (1 << 5)) != 0;
        ulong? activeRaw = reader(0x0B);
        PowerProfile? activeMode = activeRaw.HasValue ? DecodePowerMode(activeRaw.Value) : null;
        return new AcerCapabilities(true, turboSupported, activeMode);
    }

    public static PowerProfile? GetEcPowerMode()
    {
        if (AcerCapabilitiesProvider.Current.EcHid is not { Modes.Count: > 0 } info) return null;

        try
        {
            using var hid = AcerEcHid.OpenWith(info.Modes);
            return hid?.ReadMode();
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed reading EC HID power mode: {ex.Message}", "AcerWmi");
            return null;
        }
    }

    public static PowerProfile? GetActivePowerMode()
    {
        if (_activePowerModeReader != null) return _activePowerModeReader();

        try
        {
            var capabilities = AcerCapabilitiesProvider.Current;
            if (capabilities.PowerModeChannel == PowerModeChannel.EcHid)
            {
                var ecMode = GetEcPowerMode();
                if (ecMode.HasValue) return ecMode.Value;
            }

            ulong? rawOutput = ReadMiscSetting(0x0B);
            if (rawOutput.HasValue) return DecodePowerMode(rawOutput.Value);
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed reading active power mode: {ex.Message}", "AcerWmi");
        }

        return null;
    }

    private static bool TrySetPowerModeViaEcHid(PowerProfile profile, EcHidInfo info)
    {
        try
        {
            using var hid = AcerEcHid.OpenWith(info.Modes);
            if (hid == null)
            {
                AcerCapabilitiesProvider.Invalidate();
                return false;
            }

            if (!hid.WriteMode(profile))
            {
                LogManager.Warn($"EC HID refused power mode {profile}", "AcerWmi");
                AcerCapabilitiesProvider.Invalidate();
                return false;
            }

            var readBack = hid.ReadMode();
            LogManager.Info($"EC HID power mode set to {profile} (read back {readBack?.ToString() ?? "unknown"})", "AcerWmi");
            return true;
        }
        catch (Exception ex)
        {
            LogManager.Warn($"EC HID mode set exception: {ex.Message}", "AcerWmi");
            AcerCapabilitiesProvider.Invalidate();
            return false;
        }
    }

    private static bool SetPowerModeViaWmiLocked(PowerProfile profile, AcerModelProfile model)
    {
        if (model.RequiresBalancedModeReset && profile != PowerProfile.Balanced &&
            !InvokeWmiSync(
                AcerWmiProtocol.GamingClass,
                AcerWmiProtocol.SetGamingMiscSetting,
                AcerWmiProtocol.PowerModePayload(PowerProfile.Balanced)))
        {
            return false;
        }

        return InvokeWmiSync(
            AcerWmiProtocol.GamingClass,
            AcerWmiProtocol.SetGamingMiscSetting,
            AcerWmiProtocol.PowerModePayload(profile));
    }

    public static async Task SetPowerModeAsync(PowerProfile profile)
    {
        await Task.Run(() =>
        {
            var capabilities = AcerCapabilitiesProvider.Current;

            if (capabilities.PowerModeChannel == PowerModeChannel.EcHid &&
                capabilities.EcHid is { Modes.Count: > 0 } ecHid &&
                TrySetPowerModeViaEcHid(profile, ecHid))
            {
                return;
            }

            lock (_wmiLock)
            {
                if (SetPowerModeViaWmiLocked(profile, capabilities.Profile))
                    return;
            }

            if (capabilities.PowerModeChannel == PowerModeChannel.ActionFunction7)
            {
                LogManager.Warn(
                    $"Power mode {profile} was not applied: mode is on APGeAction function 7 (read-only)",
                    "AcerWmi");
                return;
            }

            LogManager.Warn($"WMI failed to set power mode {profile}", "AcerWmi");
        }).ConfigureAwait(false);
    }

    public static async Task ApplyPowerProfileAsync(PowerProfile profile, int cpuFanSpeed = 0, int gpuFanSpeed = 0)
    {
        bool expertMode = SettingsManager.GetInt("ExpertMode", 0) == 1;
        var (onAc, batteryPercent) = ReadPowerState();

        var resolved = HardwareSafetyPolicy.ResolveProfileWrite(profile, onAc, batteryPercent, expertMode);
        if (resolved != profile)
        {
            LogManager.Warn(
                $"Power mode {profile} held back (AC={onAc}, battery={batteryPercent}%, expert={expertMode}); applying {resolved}",
                "AcerWmi");
            profile = resolved;
        }

        if (profile == PowerProfile.Turbo && !IsTurboModeSupported())
        {
            LogManager.Warn("Turbo profile unsupported; falling back to Performance", "AcerWmi");
            profile = PowerProfile.Performance;
        }

        if (profile == PowerProfile.Eco && !IsEcoModeSupported())
        {
            LogManager.Warn("Eco profile unsupported; falling back to Balanced", "AcerWmi");
            profile = PowerProfile.Balanced;
        }

        await SetPowerModeAsync(profile).ConfigureAwait(false);
        SettingsManager.Save("LastPowerMode", (int)profile);

        if (!HardwareSafetyPolicy.AllowCustomFanWrite(profile, expertMode))
        {
            ResetFansToAutoSync();
        }
        else if (cpuFanSpeed > 0 || gpuFanSpeed > 0)
        {
            int cpu = Math.Clamp(cpuFanSpeed, 0, 100);
            int gpu = Math.Clamp(gpuFanSpeed, 0, 100);
            if (await SetFansAsync(FanProfile.Medium, cpu, gpu).ConfigureAwait(false))
            {
                SettingsManager.Save("CustomFanSpeedCpu", cpu);
                SettingsManager.Save("CustomFanSpeedGpu", gpu);
            }
        }
    }

    /// <summary>Current power state (AC online status and battery percent).</summary>
    public static (bool OnAc, int BatteryPercent) GetPowerState() => ReadPowerState();

    private static (bool OnAc, int BatteryPercent) ReadPowerState()
    {
        var reader = _powerStateReader;
        if (reader != null) return reader();

        try
        {
            var state = BatteryManager.GetBatteryState();
            return (state.AcOnLine, HardwareSafetyPolicy.BatteryPercent(state.MaxCapacity, state.RemainingCapacity));
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Power state read failed: {ex.Message}", "AcerWmi");
            return (true, 100);
        }
    }

    /// <summary>
    /// The mode that would actually be written right now. UI uses this to preview a
    /// downgrade instead of showing a mode the firmware will refuse.
    /// </summary>
    public static PowerProfile ResolvePowerProfileRequest(PowerProfile requested)
    {
        var (onAc, batteryPercent) = ReadPowerState();
        return HardwareSafetyPolicy.ResolveProfileWrite(
            requested, onAc, batteryPercent, SettingsManager.GetInt("ExpertMode", 0) == 1);
    }

    /// <summary>True when Performance/Turbo may be written right now.</summary>
    public static bool IsOverclockModeAvailable()
    {
        var (onAc, batteryPercent) = ReadPowerState();
        return HardwareSafetyPolicy.AllowsOverclockMode(
            onAc, batteryPercent, SettingsManager.GetInt("ExpertMode", 0) == 1);
    }

    private static ulong? ReadGamingProfile()
    {
        try
        {
            var instance = GetGamingFunctionInstance();
            if (instance == null) return null;

            lock (_wmiLock)
            {
                using var inParams = instance.GetMethodParameters(AcerWmiProtocol.GetGamingProfile);
                inParams["gmInput"] = AcerWmiProtocol.ProfileQuery;
                using var outParams = instance.InvokeMethod(AcerWmiProtocol.GetGamingProfile, inParams, null);

                var raw = outParams?.Properties["gmOutput"]?.Value ?? outParams?.Properties["outValue"]?.Value;
                return raw != null ? Convert.ToUInt64(raw) : null;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"ReadGamingProfile failed: {ex.Message}", "AcerWmi");
            return null;
        }
    }

    public static (bool Supported, bool On)? GetLcdOverdrive()
    {
        GamingProfileReaderDelegate? reader;
        lock (_wmiLock)
        {
            reader = _gamingProfileReader;
        }
        ulong? raw = reader != null ? reader() : ReadGamingProfile();
        if (!raw.HasValue) return null;

        var supported = AcerWmiProtocol.LcdOverdriveSupported(raw.Value);
        return (supported, supported && AcerWmiProtocol.LcdOverdriveOn(raw.Value));
    }

    public static bool SetLcdOverdrive(bool on)
    {
        try
        {
            ulong payload = AcerWmiProtocol.LcdOverdrivePayload(on);
            bool success = InvokeWmiSync(
                AcerWmiProtocol.GamingClass,
                AcerWmiProtocol.SetGamingProfile,
                payload);
            if (success)
            {
                LogManager.Info($"LCD overdrive set to {(on ? "ON" : "OFF")}", "AcerWmi");
            }
            return success;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"SetLcdOverdrive failed: {ex.Message}", "AcerWmi");
            return false;
        }
    }

    private static ulong? ReadUsbChargingRaw()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                AcerWmiProtocol.WmiNamespace, $"SELECT * FROM {AcerWmiProtocol.ActionClass}");

            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    using var inParams = instance.GetMethodParameters("GetFunction");
                    inParams["uiInput"] = (ulong)AcerWmiProtocol.UsbChargingQuery;
                    using var outParams = instance.InvokeMethod("GetFunction", inParams, null);

                    var raw = outParams?.Properties["uiOutput"]?.Value;
                    return raw != null ? Convert.ToUInt64(raw) : null;
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"ReadUsbChargingRaw failed: {ex.Message}", "AcerWmi");
        }

        return null;
    }

    public static UsbChargingState? GetUsbCharging()
    {
        UsbChargingReaderDelegate? reader;
        lock (_wmiLock)
        {
            reader = _usbChargingReader;
        }
        ulong? raw = reader != null ? reader() : ReadUsbChargingRaw();
        if (!raw.HasValue) return null;

        ulong value = raw.Value;
        var state = (int)((value >> 8) & 0xFF);
        var floor = (int)((value >> 16) & 0xFF);

        if (state != AcerWmiProtocol.UsbChargingOn &&
            state != AcerWmiProtocol.UsbChargingOff)
        {
            LogManager.Info(
                $"WMI: USB charging answered 0x{value:X} (state 0x{state:X2}); treating as unsupported",
                "AcerWmi");
            return null;
        }

        return new UsbChargingState(
            state == AcerWmiProtocol.UsbChargingOn,
            Array.IndexOf(AcerWmiProtocol.UsbChargingFloors, floor) >= 0 ? floor : null);
    }

    public static bool SetUsbCharging(bool on, int floor)
    {
        if (Array.IndexOf(AcerWmiProtocol.UsbChargingFloors, floor) < 0) return false;

        try
        {
            ulong payload = AcerWmiProtocol.UsbChargingPayload(on, floor);
            bool success = InvokeApGeActionSync("SetFunction", payload);
            if (success)
            {
                LogManager.Info($"USB charging set to {(on ? "ON" : "OFF")} (stop at {floor}%)", "AcerWmi");
            }
            return success;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"SetUsbCharging failed: {ex.Message}", "AcerWmi");
            return false;
        }
    }
}
