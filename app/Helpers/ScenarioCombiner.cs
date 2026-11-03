using System;
using System.Collections.Generic;
using System.Linq;
using Nitrous.Enums;
using Nitrous.Managers;

namespace Nitrous.Helpers;

/// <summary>
/// Merges several App Sync scenarios that are running at the same time.
/// Each setting group goes to the strongest contributor:
/// power = highest mode (per AC/DC), fan = highest fan strength,
/// keyboard = taken from the highest-power profile. Pure; no I/O.
/// </summary>
public static class ScenarioCombiner
{
    private static int PowerRank(PowerProfile mode) => mode switch
    {
        PowerProfile.Turbo => 3,
        PowerProfile.Performance => 2,
        PowerProfile.Balanced => 1,
        _ => 0, // Quiet and anything else
    };

    private static int FanRank(ScenarioProfile p) => p.FanMode switch
    {
        FanProfile.Max => 100,
        FanProfile.Medium => Math.Max(p.CustomFanSpeedCpu, p.CustomFanSpeedGpu),
        FanProfile.Quiet => 25,
        _ => 0, // Auto
    };

    public static ScenarioProfile Combine(IReadOnlyList<ScenarioProfile> sources)
    {
        if (sources.Count == 0) throw new ArgumentException("At least one profile is required.", nameof(sources));

        // Primary: strongest power profile (AC rank, then DC rank); first wins ties.
        var primary = sources
            .OrderByDescending(p => PowerRank(p.AcMode))
            .ThenByDescending(p => PowerRank(p.DcMode))
            .First();

        var merged = primary.Clone();
        merged.AcMode = sources.MaxBy(p => PowerRank(p.AcMode))!.AcMode;
        merged.DcMode = sources.MaxBy(p => PowerRank(p.DcMode))!.DcMode;
        merged.Name = string.Join(" + ", sources.Select(p => p.Name));

        // Fan: strongest fan setting wins as a whole group.
        var fanWinner = sources.OrderByDescending(FanRank).First();
        merged.FanMode = fanWinner.FanMode;
        merged.CustomFanSpeedCpu = fanWinner.CustomFanSpeedCpu;
        merged.CustomFanSpeedGpu = fanWinner.CustomFanSpeedGpu;
        merged.FanCurvePreset = fanWinner.FanCurvePreset;

        // Keyboard already cloned from primary (highest power profile).

        // Union of app links, first claim wins per file name.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        merged.LinkedApps = [];
        foreach (var source in sources)
        {
            foreach (string app in source.LinkedApps)
            {
                string fileName = System.IO.Path.GetFileName(app);
                if (fileName.Length > 0 && seen.Add(fileName)) merged.LinkedApps.Add(app);
            }
        }

        return merged;
    }
}
