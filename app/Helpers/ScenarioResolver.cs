using System;
using System.Collections.Generic;
using System.IO;
using Nitrous.Enums;
using Nitrous.Managers;

namespace Nitrous.Helpers;

public enum ScenarioSwitchReason
{
    Keep = 0,
    Match = 1,
    Revert = 2,
}

public readonly record struct ScenarioResolution(string TargetActiveId, ScenarioSwitchReason Reason);

/// <summary>
/// Pure Scenario state decisions: effective power mode and App Sync targeting.
/// No I/O, no hardware, no timers.
/// </summary>
public static class ScenarioResolver
{
    public static PowerProfile EffectiveMode(ScenarioProfile profile, bool isAcOnline, bool turboSupported)
    {
        var mode = isAcOnline ? profile.AcMode : profile.DcMode;
        if (mode == PowerProfile.Turbo && !turboSupported) mode = PowerProfile.Performance;
        return mode;
    }

    public static ScenarioProfile? FindProfileByName(IReadOnlyList<ScenarioProfile> profiles, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var p in profiles)
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return null;
    }

    public static ScenarioProfile? FindProfile(IReadOnlyList<ScenarioProfile> profiles, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var p in profiles)
        {
            if (string.Equals(p.Id, id, StringComparison.Ordinal)) return p;
        }
        return null;
    }

    /// <summary>
    /// Returns the App Sync profile that owns the given exe file name, or null
    /// when the exe is not linked to any enabled profile.
    /// </summary>
    public static ScenarioProfile? FindLinkedProfile(
        IReadOnlyList<ScenarioProfile> profiles,
        string? exeFileName)
    {
        if (string.IsNullOrWhiteSpace(exeFileName)) return null;

        string exe = Path.GetFileName(exeFileName.Trim());
        if (exe.Length == 0) return null;

        foreach (var profile in profiles)
        {
            if (!profile.AppSyncEnabled) continue;
            foreach (string app in profile.LinkedApps)
            {
                if (string.Equals(Path.GetFileName(app), exe, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// App Sync targeting. Matching is by exe file name only; per-profile
    /// AppSyncEnabled gates matching. No match keeps the current active profile
    /// (no revert, per owner decision 2026-11-03).
    /// </summary>
    public static ScenarioResolution ResolveForeground(
        IReadOnlyList<ScenarioProfile> profiles,
        string? activeId,
        string? selectedId,
        string? exeFileName,
        bool masterEnabled)
    {
        string fallback = FindProfile(profiles, selectedId)?.Id
                          ?? (profiles.Count > 0 ? profiles[0].Id : string.Empty);
        string current = FindProfile(profiles, activeId)?.Id ?? fallback;

        if (!masterEnabled) return new ScenarioResolution(current, ScenarioSwitchReason.Keep);

        var linkedProfile = FindLinkedProfile(profiles, exeFileName);
        if (linkedProfile == null) return new ScenarioResolution(current, ScenarioSwitchReason.Keep);

        return linkedProfile.Id == current
            ? new ScenarioResolution(current, ScenarioSwitchReason.Keep)
            : new ScenarioResolution(linkedProfile.Id, ScenarioSwitchReason.Match);
    }

    public readonly record struct RunningLinkedApp(ScenarioProfile Profile, string ExeFileName);

    /// <summary>
    /// Conflict fallback when the App Sync winner exits: first profile in list
    /// order (excluding the exited one, enabled only) whose linked app still runs.
    /// </summary>
    public static RunningLinkedApp? FindRunningLinkedProfile(
        IReadOnlyList<ScenarioProfile> profiles,
        string? excludeProfileId,
        Func<string, bool> isExeRunning)
    {
        foreach (var p in profiles)
        {
            if (!p.AppSyncEnabled) continue;
            if (string.Equals(p.Id, excludeProfileId, StringComparison.Ordinal)) continue;
            foreach (string app in p.LinkedApps)
            {
                string exe = Path.GetFileName(app);
                if (exe.Length > 0 && isExeRunning(exe)) return new RunningLinkedApp(p, exe);
            }
        }
        return null;
    }

    /// <summary>
    /// Linked-app exit decision. App Sync holds its profile while the app runs
    /// (any focus); revert to the selected profile only when the app has exited
    /// and the sync profile is still the active one. A manual apply in between
    /// wins and is never reverted.
    /// </summary>
    public static ScenarioResolution ResolveAppExit(
        IReadOnlyList<ScenarioProfile> profiles,
        string? activeId,
        string? selectedId,
        string? appSyncActiveId)
    {
        string fallback = FindProfile(profiles, selectedId)?.Id
                          ?? (profiles.Count > 0 ? profiles[0].Id : string.Empty);
        string current = FindProfile(profiles, activeId)?.Id ?? fallback;

        if (string.IsNullOrEmpty(appSyncActiveId) || current != appSyncActiveId)
        {
            return new ScenarioResolution(current, ScenarioSwitchReason.Keep);
        }

        return fallback == current
            ? new ScenarioResolution(current, ScenarioSwitchReason.Keep)
            : new ScenarioResolution(fallback, ScenarioSwitchReason.Revert);
    }
}
