using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;
using Nitrous.Interop;

namespace Nitrous.Managers;

public static class BatteryManager
{
    private static uint _lastNtStatus = 0;
    private static readonly object _statusLock = new();

    internal delegate uint CallNtPowerInformationDelegate(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferSize,
        out Powrprof.SYSTEM_BATTERY_STATE outputBuffer,
        uint outputBufferSize);

    private static CallNtPowerInformationDelegate _callNtPowerInfo = Powrprof.CallNtPowerInformation;

    internal static void SetPowerInfoDelegateForTesting(CallNtPowerInformationDelegate? testDelegate)
    {
        lock (_statusLock)
        {
            _callNtPowerInfo = testDelegate ?? Powrprof.CallNtPowerInformation;
            _lastNtStatus = 0;
        }
    }

    public static Powrprof.SYSTEM_BATTERY_STATE GetBatteryState()
    {
        const int SystemBatteryStateLevel = 5;
        Powrprof.SYSTEM_BATTERY_STATE state = default;
        uint ret = _callNtPowerInfo(
            SystemBatteryStateLevel,
            IntPtr.Zero,
            0,
            out state,
            (uint)Marshal.SizeOf<Powrprof.SYSTEM_BATTERY_STATE>());

        if (ret != 0)
        {
            bool shouldLog = false;
            lock (_statusLock)
            {
                if (ret != _lastNtStatus)
                {
                    _lastNtStatus = ret;
                    shouldLog = true;
                }
            }

            if (shouldLog)
            {
                LogManager.Warn($"CallNtPowerInformation failed with NTSTATUS 0x{ret:X}", "Battery");
            }

            state.AcOnLine = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                             System.Windows.Forms.PowerLineStatus.Online;
        }
        else
        {
            bool recovered = false;
            lock (_statusLock)
            {
                if (_lastNtStatus != 0)
                {
                    _lastNtStatus = 0;
                    recovered = true;
                }
            }

            if (recovered && LogManager.IsDebugEnabled)
            {
                LogManager.Debug("CallNtPowerInformation recovered to STATUS_SUCCESS", "Battery");
            }
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
            uint designed = QuerySingleUint("BatteryStaticData", "DesignedCapacity");
            uint full = QuerySingleUint("BatteryFullChargedCapacity", "FullChargedCapacity");
            uint cycleCount = QuerySingleUint("BatteryCycleCount", "CycleCount");

            return ComputeBatteryHealth(designed, full, cycleCount);
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed to read WMI battery health: {ex.Message}", "Battery");
        }

        return null;
    }

    internal delegate uint? HealthReaderDelegate(string className, string property);

    private static HealthReaderDelegate? _healthReader;

    internal static void SetHealthReaderForTesting(HealthReaderDelegate? testReader)
    {
        _healthReader = testReader;
    }

    internal delegate bool HealthControlDelegate(byte batteryNo, byte functionMask, byte functionStatus);

    private static HealthControlDelegate? _healthControl;

    internal static void SetHealthControlForTesting(HealthControlDelegate? testControl)
    {
        _healthControl = testControl;
    }

    private static uint QuerySingleUint(string className, string property)
    {
        if (_healthReader != null) return _healthReader(className, property) ?? 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT {property} FROM {className}");
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    if (obj[property] != null) return Convert.ToUInt32(obj[property]);
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"QuerySingleUint {className}.{property} failed: {ex.Message}", "Battery");
        }

        return 0;
    }

    public static BatteryHealthInfo? ComputeBatteryHealth(uint designed, uint full, uint cycleCount)
    {
        if (designed == 0) return null;
        double health = ((double)full / designed) * 100.0;
        double wear = 100.0 - health;
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

    public static async Task SetChargeLimitAsync(bool enable)
    {
        byte status = enable ? (byte)1 : (byte)0;
        SettingsManager.Save("ChargeLimit", enable ? 1 : 0);

        if (_healthControl != null)
        {
            await Task.Run(() => _healthControl(1, 1, status)).ConfigureAwait(false);
            return;
        }

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
                        inParams["uFunctionStatus"] = status;
                        inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };
                        instance.InvokeMethod("SetBatteryHealthControl", inParams, null);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to set battery charge limit: {ex.Message}", ex, "Battery");
            }
        }).ConfigureAwait(false);
    }}
