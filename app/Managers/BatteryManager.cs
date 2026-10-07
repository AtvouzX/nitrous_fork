using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;

namespace Nitrous.Managers;

public static class BatteryManager
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_BATTERY_STATE
    {
        [MarshalAs(UnmanagedType.I1)] public bool AcOnLine;
        [MarshalAs(UnmanagedType.I1)] public bool BatteryPresent;
        [MarshalAs(UnmanagedType.I1)] public bool Charging;
        [MarshalAs(UnmanagedType.I1)] public bool Discharging;
        public byte Spare1_1;
        public byte Spare1_2;
        public byte Spare1_3;
        public byte Spare1_4;
        public uint MaxCapacity;        // mWh
        public uint RemainingCapacity;  // mWh
        public int Rate;               // mW (negative when discharging)
        public uint EstimatedTime;     // seconds
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CallNtPowerInformation(
        int InformationLevel,
        IntPtr InputBuffer,
        uint InputBufferSize,
        out SYSTEM_BATTERY_STATE OutputBuffer,
        uint OutputBufferSize);

    public static SYSTEM_BATTERY_STATE GetBatteryState()
    {
        const int SystemBatteryStateLevel = 5;
        SYSTEM_BATTERY_STATE state = default;
        uint ret = CallNtPowerInformation(SystemBatteryStateLevel, IntPtr.Zero, 0, out state, (uint)Marshal.SizeOf<SYSTEM_BATTERY_STATE>());
        if (ret != 0)
        {
            Debug.WriteLine($"CallNtPowerInformation failed with code {ret}");
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
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["DesignedCapacity"] != null)
                    {
                        designed = Convert.ToUInt32(obj["DesignedCapacity"]);
                        break;
                    }
                }
            }

            // 2. Get Full Charged Capacity
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["FullChargedCapacity"] != null)
                    {
                        full = Convert.ToUInt32(obj["FullChargedCapacity"]);
                        break;
                    }
                }
            }

            // 3. Get Cycle Count
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CycleCount FROM BatteryCycleCount"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["CycleCount"] != null)
                    {
                        cycleCount = Convert.ToUInt32(obj["CycleCount"]);
                        break;
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
            Debug.WriteLine($"Failed to read WMI battery health: {ex.Message}");
        }

        return null;
    }
}
