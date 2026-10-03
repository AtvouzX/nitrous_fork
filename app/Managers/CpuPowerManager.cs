using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static class CpuPowerManager
{
    private static readonly string CachedPowerCfgPath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
    
    // Cache the applied state to prevent redundant powercfg process executions
    private static int _lastMin = -1;
    private static int _lastMax = -1;
    private static bool _lastIsOnline = false;

    public static async Task ApplyProfileLimitsAsync(PowerProfile profile, bool isOnline)
    {
        int min = 5;
        int max = 100;

        if (isOnline) // AC Power
        {
            switch (profile)
            {
                case PowerProfile.Quiet: max = 95; break;
                case PowerProfile.Balanced: max = 100; break;
                case PowerProfile.Performance: max = 100; break;
                case PowerProfile.Turbo: min = 100; max = 100; break;
            }
        }
        else // DC Power (Battery)
        {
            switch (profile)
            {
                case PowerProfile.Quiet: max = 88; break;
                case PowerProfile.Balanced: max = 99; break;
                case PowerProfile.Performance: max = 100; break;
                case PowerProfile.Turbo: max = 100; break;
            }
        }

        await SetLimitsAsync(min, max, isOnline);
    }

    public static async Task RestoreDefaultsAsync(bool isOnline)
    {
        // Standard Windows default is 5% min, 100% max
        await SetLimitsAsync(5, 100, isOnline);
    }

    private static async Task SetLimitsAsync(int minPercent, int maxPercent, bool isOnline)
    {
        // Zero-Process Execution: Prevent redundant process spawns if state hasn't changed
        if (_lastMin == minPercent && _lastMax == maxPercent && _lastIsOnline == isOnline)
            return;

        _lastMin = minPercent;
        _lastMax = maxPercent;
        _lastIsOnline = isOnline;

        await Task.Run(() =>
        {
            string powerType = isOnline ? "setacvalueindex" : "setdcvalueindex";

            RunPowerCfg($"/{powerType} SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {minPercent}");
            RunPowerCfg($"/{powerType} SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {maxPercent}");
            RunPowerCfg("/setactive SCHEME_CURRENT"); // Apply immediately
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
        catch { }
    }
}
