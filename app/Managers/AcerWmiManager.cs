using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using Nitrous.Enums;
using Nitrous.Helpers;

namespace Nitrous.Managers;

public static partial class AcerWmiManager
{
    // Acer EC Sensor Addresses for Hardware Telemetry
    public const uint SensorCpuTemp = 0x0101u;
    public const uint SensorCpuRpm = 0x0201u;
    public const uint SensorGpuTempPrimary = 0x0A01u;
    public const uint SensorGpuTempFallback1 = 0x0901u;
    public const uint SensorGpuTempFallback2 = 0x0B01u;
    public const uint SensorGpuRpm = 0x0601u;

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

    internal delegate bool WmiInvokeDelegate(string className, string methodName, ulong gmInput);
    internal delegate bool WmiBufferInvokeDelegate(string className, string methodName, byte[] gmInput);

    private static WmiInvokeDelegate? _wmiInvokeOverride;
    private static WmiBufferInvokeDelegate? _wmiBufferInvokeOverride;

    internal static void SetWmiBufferInvokeForTesting(WmiBufferInvokeDelegate? testDelegate)
    {
        lock (_wmiLock)
        {
            _wmiBufferInvokeOverride = testDelegate;
        }
    }

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

    private static bool InvokeApGeActionSync(string methodName, ulong uiInput)
    {
        WmiInvokeDelegate? testOverride;
        lock (_wmiLock)
        {
            testOverride = _wmiInvokeOverride;
        }
        if (testOverride != null) return testOverride("APGeAction", methodName, uiInput);

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM APGeAction");
            using var collection = searcher.Get();
            bool invoked = false;
            foreach (ManagementObject instance in collection)
            {
                using (instance)
                {
                    using var inParams = instance.GetMethodParameters(methodName);
                    inParams["uiInput"] = uiInput;
                    instance.InvokeMethod(methodName, inParams, null);
                    invoked = true;
                }
            }
            return invoked;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"InvokeApGeAction {methodName}(0x{uiInput:X}) skipped: {ex.Message}", "AcerWmi");
            return false;
        }
    }

    private static bool InvokeWmiBufferSync(string className, string methodName, byte[] gmInput)
    {
        WmiBufferInvokeDelegate? testOverride;
        lock (_wmiLock)
        {
            testOverride = _wmiBufferInvokeOverride;
        }
        if (testOverride != null) return testOverride(className, methodName, gmInput);

        try
        {
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
            LogManager.Error($"Error invoking {methodName}(byte[{gmInput.Length}]): {ex.Message}", ex, "AcerWmi");
            return false;
        }
    }

    private static async Task InvokeWmiBufferAsync(string className, string methodName, byte[] gmInput)
    {
        await Task.Run(() => InvokeWmiBufferSync(className, methodName, gmInput)).ConfigureAwait(false);
    }

    private static async Task InvokeWmiAsync(string className, string methodName, ulong gmInput)
    {
        await Task.Run(() => InvokeWmiSync(className, methodName, gmInput)).ConfigureAwait(false);
    }
}
