using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static class AcerWmiManager
{
    // Acer EC Sensor Addresses for Hardware Telemetry
    public const uint SensorCpuTemp = 0x0101u;
    public const uint SensorCpuRpm = 0x0201u;
    public const uint SensorGpuTempPrimary = 0x0A01u;
    public const uint SensorGpuTempFallback1 = 0x0901u;
    public const uint SensorGpuTempFallback2 = 0x0B01u;
    public const uint SensorGpuRpm = 0x0601u;

    // Acer EC Fan Behavior Registers
    public const ulong FanBehaviorAuto = 0x41000Ful;
    public const ulong FanBehaviorMax = 0x82000Ful;
    public const ulong FanBehaviorCustom = 0xC3000Ful;

    private static ManagementObject? _cachedGamingFunction;
    private static readonly object _wmiLock = new();

    private static ManagementObject? GetGamingFunctionInstance()
    {
        lock (_wmiLock)
        {
            if (_cachedGamingFunction != null)
            {
                try
                {
                    _ = _cachedGamingFunction.ClassPath;
                    return _cachedGamingFunction;
                }
                catch
                {
                    try { _cachedGamingFunction.Dispose(); } catch { }
                    _cachedGamingFunction = null;
                }
            }

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AcerGamingFunction");
                using var collection = searcher.Get();
                foreach (ManagementObject instance in collection)
                {
                    _cachedGamingFunction = instance;
                    return _cachedGamingFunction;
                }
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to get AcerGamingFunction: {ex.Message}", "AcerWmi");
            }

            return null;
        }
    }

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

    internal delegate bool WmiInvokeDelegate(string className, string methodName, ulong gmInput);

    private static WmiInvokeDelegate? _wmiInvokeOverride;

    internal static void SetWmiInvokeForTesting(WmiInvokeDelegate? testDelegate)
    {
        lock (_wmiLock)
        {
            _wmiInvokeOverride = testDelegate;
        }
    }

    internal delegate ulong? MiscSettingReaderDelegate(uint register);

    private static MiscSettingReaderDelegate? _miscSettingReader;

    internal static void SetMiscSettingReaderForTesting(MiscSettingReaderDelegate? testReader)
    {
        lock (_wmiLock)
        {
            _miscSettingReader = testReader;
        }
    }

    public readonly record struct AcerCapabilities(bool Supported, bool TurboSupported, PowerProfile? ActiveMode);

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
        await InvokeWmiAsync("AcerGamingFunction", "SetGamingMiscSetting", payload);
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

    public static bool ResetFansToAutoSync()
    {
        if (!IsHardwareSupported()) return false;
        try
        {
            LogManager.Info("Resetting fan mode to Auto (synchronous failsafe)", "AcerWmi");
            return InvokeWmiSync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorAuto);
        }
        catch (Exception ex)
        {
            LogManager.Error($"Failed resetting fans to Auto: {ex.Message}", ex, "AcerWmi");
            return false;
        }
    }

    public static async Task SetFansAsync(FanProfile profile, int cpuSpeed = 0, int gpuSpeed = 0)
    {
        LogManager.Info($"Setting fans to {profile} (CPU={cpuSpeed}%, GPU={gpuSpeed}%)", "AcerWmi");
        if (profile == FanProfile.Auto)
        {
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorAuto);
        }
        else if (profile == FanProfile.Max)
        {
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorMax);
        }
        else // Custom (Quiet/Medium or explicit speeds)
        {
            int cpu = Math.Clamp(cpuSpeed, 0, 100);
            int gpu = Math.Clamp(gpuSpeed, 0, 100);
            if (cpu <= 0 && gpu <= 0)
            {
                int unified = Math.Clamp((int)profile, 0, 100);
                cpu = unified;
                gpu = unified;
            }
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorCustom);
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", 1ul | ((ulong)cpu << 8));
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", 2ul | ((ulong)gpu << 8));
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", 4ul | ((ulong)gpu << 8));
        }
    }

    public static (int CpuTemp, int CpuRpm, int GpuTemp, int GpuRpm) GetSystemTelemetry()
    {
        int cpuTemp = 0, cpuRpm = 0, gpuTemp = 0, gpuRpm = 0;

        try
        {
            var instance = GetGamingFunctionInstance();
            if (instance != null)
            {
                lock (_wmiLock)
                {
                    // 1. CPU Temperature (Address: 0x0101)
                    cpuTemp = ReadAcerSensor(instance, SensorCpuTemp, 0xFF);

                    // 2. CPU Fan Speed (Address: 0x0201)
                    cpuRpm = ReadAcerSensor(instance, SensorCpuRpm, 0xFFFF);

                    // 3. GPU Temperature (Address: 0x0A01, with 0x0901/0x0B01 fallbacks)
                    gpuTemp = ReadAcerSensor(instance, SensorGpuTempPrimary, 0xFF);
                    if (gpuTemp == 0) gpuTemp = ReadAcerSensor(instance, SensorGpuTempFallback1, 0xFF);
                    if (gpuTemp == 0) gpuTemp = ReadAcerSensor(instance, SensorGpuTempFallback2, 0xFF);

                    // 4. GPU Fan Speed (Address: 0x0601)
                    gpuRpm = ReadAcerSensor(instance, SensorGpuRpm, 0xFFFF);
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"ReadHardwareSensors failed: {ex.Message}", "AcerWmi");
        }

        return (cpuTemp, cpuRpm, gpuTemp, gpuRpm);
    }

    private static ManagementBaseObject? _cachedInParams;

    // Helper method to isolate exceptions per-sensor and handle WMI output safely
    private static int ReadAcerSensor(ManagementObject instance, uint address, ulong bitmask)
    {
        try
        {
            _cachedInParams ??= instance.GetMethodParameters("GetGamingSysInfo");
            _cachedInParams["gmInput"] = address; // Must be uint (UInt32)

            using var outParams = instance.InvokeMethod("GetGamingSysInfo", _cachedInParams, null);
            if (outParams != null)
            {
                // Safely check for gmOutput or outValue depending on the BIOS version
                object? rawValue = outParams.Properties["gmOutput"]?.Value ?? outParams.Properties["outValue"]?.Value;

                if (rawValue != null)
                {
                    ulong rawOutput = Convert.ToUInt64(rawValue);
                    return (int)((rawOutput >> 8) & bitmask);
                }
            }
        }
        catch
        {
            try { _cachedInParams?.Dispose(); } catch { }
            _cachedInParams = null;
        }

        return 0;
    }

    private static bool InvokeWmiSync(string className, string methodName, ulong gmInput)
    {
        WmiInvokeDelegate? testOverride;
        lock (_wmiLock)
        {
            testOverride = _wmiInvokeOverride;
        }
        if (testOverride != null) return testOverride(className, methodName, gmInput);

        try
        {
            // Grabbing a fresh instance ensures the COM object is alive and connected
            using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {className}");
            using var collection = searcher.Get();
            bool invoked = false;
            foreach (ManagementObject instance in collection)
            {
                using (instance)
                {
                    using var inParams = instance.GetMethodParameters(methodName);
                    inParams["gmInput"] = gmInput;
                    instance.InvokeMethod(methodName, inParams, null);
                    invoked = true;
                }
            }
            return invoked;
        }
        catch (Exception ex)
        {
            LogManager.Error($"Error invoking {methodName}(0x{gmInput:X}): {ex.Message}", ex, "AcerWmi");
            return false;
        }
    }

    private static async Task InvokeWmiAsync(string className, string methodName, ulong gmInput)
    {
        await Task.Run(() => InvokeWmiSync(className, methodName, gmInput));
    }
}
