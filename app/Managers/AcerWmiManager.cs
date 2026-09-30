using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static class AcerWmiManager
{
    public static bool IsHardwareSupported()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AcerGamingFunction");
            using var collection = searcher.Get();
            return collection.Count > 0;
        }
        catch { return false; }
    }

    public static bool IsTurboModeSupported()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AcerGamingFunction");
            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    // 0x0A represents the "Supported Profiles" registry
                    using var inParams = instance.GetMethodParameters("GetGamingMiscSetting");
                    inParams["gmInput"] = 0x0Au;

                    using var outParams = instance.InvokeMethod("GetGamingMiscSetting", inParams, null);
                    if (outParams != null)
                    {
                        object? rawValue = outParams.Properties["gmOutput"]?.Value ?? outParams.Properties["outValue"]?.Value;

                        if (rawValue != null)
                        {
                            ulong rawOutput = Convert.ToUInt64(rawValue);

                            // Shift right by 8 to extract the 8-bit hex response
                            int supportedBitmap = (int)((rawOutput >> 8) & 0xFF);

                            // Check if Bit 5 (Turbo Mode) is set in the bitmap
                            return (supportedBitmap & (1 << 5)) != 0;
                        }
                    }
                    break;
                }
            }
        }
        catch
        {
            // Fail safely if the WMI method isn't supported at all
        }

        return false;
    }

    public static async Task SetPowerModeAsync(PowerProfile profile)
    {
        ulong payload = ((ulong)profile << 8) | 0x0B;
        await InvokeWmiAsync("AcerGamingFunction", "SetGamingMiscSetting", payload.ToString());
    }

    public static async Task SetFansAsync(FanProfile profile)
    {
        if (profile == FanProfile.Auto)
        {
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", 0x41000Ful.ToString());
        }
        else if (profile == FanProfile.Max)
        {
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", 0x82000Ful.ToString());
        }
        else // Custom (Unified)
        {
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", 0xC3000Ful.ToString());
            ulong speedPercent = (ulong)profile;
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", (1ul | (speedPercent << 8)).ToString());
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", (2ul | (speedPercent << 8)).ToString());
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", (4ul | (speedPercent << 8)).ToString());
        }
    }

    public static async Task SetCustomFansAsync(int cpuSpeed, int gpuSpeed)
    {
        await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", 0xC3000Ful.ToString());
        await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", (1ul | ((ulong)cpuSpeed << 8)).ToString());
        await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", (2ul | ((ulong)gpuSpeed << 8)).ToString());
        await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", (4ul | ((ulong)gpuSpeed << 8)).ToString());
    }

    public static (int CpuTemp, int CpuRpm, int GpuTemp, int GpuRpm) GetSystemTelemetry()
    {
        int cpuTemp = 0, cpuRpm = 0, gpuTemp = 0, gpuRpm = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AcerGamingFunction");
            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    // 1. CPU Temperature (Address: 0x0101)
                    cpuTemp = ReadAcerSensor(instance, 0x0101u, 0xFF);

                    // 2. CPU Fan Speed (Address: 0x0201)
                    cpuRpm = ReadAcerSensor(instance, 0x0201u, 0xFFFF);

                    // 3. GPU Temperature (Address: 0x0A01)
                    gpuTemp = ReadAcerSensor(instance, 0x0A01u, 0xFF);
                    if (gpuTemp == 0) gpuTemp = ReadAcerSensor(instance, 0x0901u, 0xFF);
                    if (gpuTemp == 0) gpuTemp = ReadAcerSensor(instance, 0x0B01u, 0xFF);

                    // 4. GPU Fan Speed (Address: 0x0601)
                    gpuRpm = ReadAcerSensor(instance, 0x0601u, 0xFFFF);

                    break; // Only process the first instance
                }
            }
        }
        catch
        {
            // Fail silently if WMI is entirely inaccessible
        }

        return (cpuTemp, cpuRpm, gpuTemp, gpuRpm);
    }

    // Helper method to isolate exceptions per-sensor and handle WMI output safely
    private static int ReadAcerSensor(ManagementObject instance, uint address, ulong bitmask)
    {
        try
        {
            using var inParams = instance.GetMethodParameters("GetGamingSysInfo");
            inParams["gmInput"] = address; // Must be uint (UInt32)

            using var outParams = instance.InvokeMethod("GetGamingSysInfo", inParams, null);
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
            // If one sensor fails (e.g., GPU is asleep), it won't crash the other sensors
        }

        return 0;
    }

    public static async Task SetChargeLimitAsync(bool enable)
    {
        byte param = enable ? (byte)1 : (byte)0;
        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryControl");
                using var collection = searcher.Get();
                foreach (ManagementObject instance in collection)
                {
                    using (instance)
                    {
                        using var inParams = instance.GetMethodParameters("SetBatteryHealthControl");
                        inParams["uBatteryNo"] = (byte)1;
                        inParams["uFunctionMask"] = (byte)1;
                        inParams["uFunctionStatus"] = param;
                        inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };
                        instance.InvokeMethod("SetBatteryHealthControl", inParams, null);

                        break;
                    }
                }
            }
            catch { }
        });
    }

    private static async Task InvokeWmiAsync(string className, string methodName, string gmInputStr)
    {
        await Task.Run(() =>
        {
            try
            {
                // Grabbing a fresh instance ensures the COM object is alive and connected
                using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {className}");
                using var collection = searcher.Get();
                foreach (ManagementObject instance in collection)
                {
                    using (instance)
                    {
                        using var inParams = instance.GetMethodParameters(methodName);
                        inParams["gmInput"] = gmInputStr;
                        instance.InvokeMethod(methodName, inParams, null);
                    }
                }
            }
            catch { }
        });
    }
}
