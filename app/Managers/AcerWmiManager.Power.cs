using System;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static partial class AcerWmiManager
{
    public readonly record struct AcerCapabilities(bool Supported, bool TurboSupported, PowerProfile? ActiveMode);

    public static bool IsHardwareSupported()
    {
        return GetGamingFunctionInstance() != null;
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

    public static ulong EncodePowerMode(PowerProfile profile) => ((ulong)profile << 8) | 0x0B;

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
                using var inParams = instance.GetMethodParameters("GetGamingMiscSetting");
                inParams["gmInput"] = register;
                using var outParams = instance.InvokeMethod("GetGamingMiscSetting", inParams, null);
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

    public static PowerProfile? GetActivePowerMode()
    {
        try
        {
            ulong? rawOutput = ReadMiscSetting(0x0B);
            if (rawOutput.HasValue) return DecodePowerMode(rawOutput.Value);
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed reading active power mode: {ex.Message}", "AcerWmi");
        }

        return null;
    }

    public static async Task SetPowerModeAsync(PowerProfile profile)
    {
        ulong payload = EncodePowerMode(profile);
        LogManager.Info($"Setting power mode to {profile} (0x{payload:X})", "AcerWmi");
        await InvokeWmiAsync("AcerGamingFunction", "SetGamingMiscSetting", payload).ConfigureAwait(false);
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
