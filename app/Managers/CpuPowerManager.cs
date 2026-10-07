using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static class CpuPowerManager
{
    public static readonly string CachedPowerCfgPath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
    public const string BoostModeGuid = "be337238-0d82-4146-a110-4a477b471251";

    public static readonly Guid GUID_PROCESSOR_SETTINGS_SUBGROUP = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid GUID_PROCTHROTTLEMIN = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    public static readonly Guid GUID_PROCTHROTTLEMAX = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    public static readonly Guid GUID_PERFBOOSTMODE = new("be337238-0d82-4146-a110-4a477b471251");

    [DllImport("powrprof.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerWriteACValueIndex(
        IntPtr RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        uint AcValueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerWriteDCValueIndex(
        IntPtr RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        uint DcValueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    // Cache the applied state to prevent redundant powercfg process executions
    private static int _lastAcMin = -1;
    private static int _lastAcMax = -1;
    private static int _lastAcBoost = -1;
    private static int _lastDcMin = -1;
    private static int _lastDcMax = -1;
    private static int _lastDcBoost = -1;

    public static (int acMin, int acMax, int acBoost, int dcMin, int dcMax, int dcBoost) GetProfileLimits(PowerProfile profile)
    {
        // By user requirement: Turbo Boost is disabled (PROCTHROTTLEMAX <= 99% and BoostMode = 0)
        // This stops aggressive CPU clock spikes and thermal surges on both Intel and AMD Ryzen.
        int acMin = 5;
        int acMax = 99;
        int acBoost = 0; // 0 = Disabled

        int dcMin = 5;
        int dcMax = 95;
        int dcBoost = 0; // 0 = Disabled

        switch (profile)
        {
            case PowerProfile.Quiet:
                acMin = 5;
                acMax = 85;
                acBoost = 0;
                dcMin = 5;
                dcMax = 80;
                dcBoost = 0;
                break;
            case PowerProfile.Balanced:
                acMin = 5;
                acMax = 99;
                acBoost = 0;
                dcMin = 5;
                dcMax = 95;
                dcBoost = 0;
                break;
            case PowerProfile.Performance:
                acMin = 5;
                acMax = 100;
                acBoost = 2;
                dcMin = 5;
                dcMax = 99;
                dcBoost = 0;
                break;
            case PowerProfile.Turbo:
                acMin = 100;
                acMax = 100;
                acBoost = 2; // Aggressive only for dedicated Turbo on AC
                dcMin = 5;
                dcMax = 99;
                dcBoost = 0;
                break;
        }

        return (acMin, acMax, acBoost, dcMin, dcMax, dcBoost);
    }

    public static async Task ApplyProfileLimitsAsync(PowerProfile profile, bool isOnline = true)
    {
        var (acMin, acMax, acBoost, dcMin, dcMax, dcBoost) = GetProfileLimits(profile);
        await SetDualLimitsAsync(acMin, acMax, acBoost, dcMin, dcMax, dcBoost);
    }

    public static async Task RestoreDefaultsAsync(bool isOnline = true)
    {
        // Standard Windows default is 5% min, 100% max, Boost enabled (2 = Aggressive)
        await SetDualLimitsAsync(5, 100, 2, 5, 100, 2);
    }

    private static async Task SetDualLimitsAsync(int acMin, int acMax, int acBoost, int dcMin, int dcMax, int dcBoost)
    {
        // Zero-Process Execution: Prevent redundant process spawns if state hasn't changed
        if (_lastAcMin == acMin && _lastAcMax == acMax && _lastAcBoost == acBoost &&
            _lastDcMin == dcMin && _lastDcMax == dcMax && _lastDcBoost == dcBoost)
        {
            return;
        }

        _lastAcMin = acMin;
        _lastAcMax = acMax;
        _lastAcBoost = acBoost;
        _lastDcMin = dcMin;
        _lastDcMax = dcMax;
        _lastDcBoost = dcBoost;

        await Task.Run(() =>
        {
            bool success = ApplyInProcessLimits(acMin, acMax, acBoost, dcMin, dcMax, dcBoost);
            if (!success)
            {
                RunPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {acMin}");
                RunPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {acMax}");
                RunPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR {BoostModeGuid} {acBoost}");

                RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {dcMin}");
                RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {dcMax}");
                RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR {BoostModeGuid} {dcBoost}");

                RunPowerCfg("/setactive SCHEME_CURRENT");
            }
        });
    }

    private static bool ApplyInProcessLimits(int acMin, int acMax, int acBoost, int dcMin, int dcMax, int dcBoost)
    {
        uint ret = PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeGuidPtr);
        if (ret != 0 || activeGuidPtr == IntPtr.Zero) return false;

        try
        {
            Guid activeScheme = Marshal.PtrToStructure<Guid>(activeGuidPtr);
            Guid subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
            Guid setMin = GUID_PROCTHROTTLEMIN;
            Guid setMax = GUID_PROCTHROTTLEMAX;
            Guid setBoost = GUID_PERFBOOSTMODE;

            // Apply AC settings
            PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref subGroup, ref setMin, (uint)acMin);
            PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref subGroup, ref setMax, (uint)acMax);
            PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref subGroup, ref setBoost, (uint)acBoost);

            // Apply DC settings
            PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref subGroup, ref setMin, (uint)dcMin);
            PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref subGroup, ref setMax, (uint)dcMax);
            PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref subGroup, ref setBoost, (uint)dcBoost);

            // Flush and commit settings instantly
            PowerSetActiveScheme(IntPtr.Zero, ref activeScheme);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply in-process power limits: {ex.Message}");
            return false;
        }
        finally
        {
            LocalFree(activeGuidPtr);
        }
    }

    private static void RunPowerCfg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(CachedPowerCfgPath, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PowerCfg error: {ex.Message}");
        }
    }
}
