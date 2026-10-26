using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;
using Nitrous.Interop;

namespace Nitrous.Managers;

public static class BatteryManager
{
    public static Powrprof.SYSTEM_BATTERY_STATE GetBatteryState()
    {
        const int SystemBatteryStateLevel = 5;
        Powrprof.SYSTEM_BATTERY_STATE state = default;
        uint ret = Powrprof.CallNtPowerInformation(SystemBatteryStateLevel, IntPtr.Zero, 0, out state, (uint)Marshal.SizeOf<Powrprof.SYSTEM_BATTERY_STATE>());
        if (ret != 0)
        {
            LogManager.Warn($"CallNtPowerInformation failed with NTSTATUS 0x{ret:X}", "Battery");
            state.AcOnLine = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                             System.Windows.Forms.PowerLineStatus.Online;
        }
        return state;
    }

    public struct BatteryHealthInfo
    {
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint CycleCount;
        public double HealthPercent;
        public double WearPercent;
    }

    public static BatteryHealthInfo? GetBatteryHealth()
    {
        try
        {
            uint designed = 0;
            uint full = 0;
            uint cycleCount = 0;

            // 1. Get Designed Capacity
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData"))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        if (obj["DesignedCapacity"] != null)
                        {
                            designed = Convert.ToUInt32(obj["DesignedCapacity"]);
                            break;
                        }
                    }
                }
            }

            // 2. Get Full Charged Capacity
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        if (obj["FullChargedCapacity"] != null)
                        {
                            full = Convert.ToUInt32(obj["FullChargedCapacity"]);
                            break;
                        }
                    }
                }
            }

            // 3. Get Cycle Count
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CycleCount FROM BatteryCycleCount"))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        if (obj["CycleCount"] != null)
                        {
                            cycleCount = Convert.ToUInt32(obj["CycleCount"]);
                            break;
                        }
                    }
                }
            }

            if (designed > 0)
            {
                double health = ((double)full / designed) * 100.0;
                double wear = 100.0 - health;

                // Clamp to prevent negative wear if full > designed
                if (wear < 0) wear = 0;
                if (health > 100.0) health = 100.0;

                return new BatteryHealthInfo
                {
                    DesignedCapacity = designed,
                    FullChargedCapacity = full,
                    CycleCount = cycleCount,
                    HealthPercent = health,
                    WearPercent = wear
                };
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed to read WMI battery health: {ex.Message}", "Battery");
        }

        return null;
    }
}
