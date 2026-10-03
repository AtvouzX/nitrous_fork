using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static class CpuPowerManager
{
    private static readonly string CachedPowerCfgPath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
    private const string BoostModeGuid = "be337238-0d82-4146-a110-4a477b471251";

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
            // Configure Plugged In (AC) values
            RunPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {acMin}");
            RunPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {acMax}");
            RunPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR {BoostModeGuid} {acBoost}");

            // Configure On Battery (DC) values
            RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {dcMin}");
            RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {dcMax}");
            RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR {BoostModeGuid} {dcBoost}");

            // Commit and activate immediately
            RunPowerCfg("/setactive SCHEME_CURRENT");
        });
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
        catch
        {
        }
    }
}
