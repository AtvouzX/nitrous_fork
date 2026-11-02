using System;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static partial class AcerWmiManager
{
    public readonly record struct AcerCapabilities(bool Supported, bool TurboSupported, PowerProfile? ActiveMode);

    public static bool IsHardwareSupported()
    {
        return GetGamingFunctionInstance() != null || AcerCapabilitiesProvider.Current.CanSetPowerMode;
    }

    private static bool? _isTurboSupportedCached;

    public static bool IsTurboModeSupported()
    {
        if (_isTurboSupportedCached.HasValue)
        {
            return _isTurboSupportedCached.Value;
        }

        try
        {
            var capabilities = AcerCapabilitiesProvider.Current;
            if (capabilities.PowerModeChannel == PowerModeChannel.EcHid)
            {
                return capabilities.SupportsTurbo;
            }

            ulong? rawOutput = ReadMiscSetting(0x0A);
            if (rawOutput.HasValue)
            {
                // 0x0A represents the "Supported Profiles" registry
                int supportedBitmap = (int)((rawOutput.Value >> 8) & 0xFF);

                // Check if Bit 5 (Turbo Mode) is set in the bitmap
                bool isSupported = (supportedBitmap & (1 << 5)) != 0;
                _isTurboSupportedCached = isSupported;
                return isSupported;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"IsTurboModeSupported check failed: {ex.Message}", "AcerWmi");
        }

        _isTurboSupportedCached = false;
        return false;
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
        if (profile == PowerProfile.Turbo && !IsTurboModeSupported())
        {
            LogManager.Warn("Turbo profile unsupported; falling back to Performance", "AcerWmi");
            profile = PowerProfile.Performance;
        }

        await SetPowerModeAsync(profile).ConfigureAwait(false);

        if (cpuFanSpeed > 0 || gpuFanSpeed > 0)
        {
            int cpu = Math.Clamp(cpuFanSpeed, 0, 100);
            int gpu = Math.Clamp(gpuFanSpeed, 0, 100);
            await SetFansAsync(FanProfile.Medium, cpu, gpu).ConfigureAwait(false);
            SettingsManager.Save("CustomFanSpeedCpu", cpu);
            SettingsManager.Save("CustomFanSpeedGpu", gpu);
        }

        SettingsManager.Save("LastPowerMode", (int)profile);
    }
}
