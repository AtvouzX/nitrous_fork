using Nitrous.Enums;

namespace Nitrous.Helpers;

/// <summary>
/// Pure policy for hardware writes that the vendor firmware gates itself.
/// NitroSense refuses these combinations; Nitrous mirrors the same rules so it
/// never fights the EC fan table or requests an overclock mode on battery.
/// No hardware access: callers supply the power state.
/// </summary>
public static class HardwareSafetyPolicy
{
    /// <summary>Vendor gate for Performance/Turbo: AC power plus at least this charge level.</summary>
    public const int MinimumBatteryPercentForOverclock = 40;

    /// <summary>
    /// Modes where the firmware fan table owns the fans; the vendor UI disables
    /// manual fan control in these modes. Eco joins this set in Phase 4.
    /// </summary>
    public static bool LocksFanControl(PowerProfile profile) =>
        profile is PowerProfile.Quiet or PowerProfile.Turbo;

    /// <summary>
    /// True when a custom fan speed write is allowed. Expert mode overrides the
    /// lockout; it never overrides the AC requirement for overclock modes.
    /// </summary>
    public static bool AllowCustomFanWrite(PowerProfile profile, bool expertMode) =>
        expertMode || !LocksFanControl(profile);

    /// <summary>
    /// Modes the firmware only accepts on AC with sufficient charge.
    /// </summary>
    public static bool RequiresAcGate(PowerProfile profile) =>
        profile is PowerProfile.Performance or PowerProfile.Turbo;

    /// <summary>
    /// True when overclock modes may be written: AC power, plus either Expert Mode or
    /// at least <see cref="MinimumBatteryPercentForOverclock"/> charge.
    /// </summary>
    public static bool AllowsOverclockMode(bool onAc, int batteryPercent, bool expertMode) =>
        onAc && (expertMode || batteryPercent >= MinimumBatteryPercentForOverclock);

    /// <summary>
    /// Returns the mode that may actually be written to hardware.
    /// Overclock modes fall back to Balanced on battery unconditionally, and on AC
    /// below <see cref="MinimumBatteryPercentForOverclock"/> unless expert mode is on.
    /// </summary>
    public static PowerProfile ResolveProfileWrite(
        PowerProfile requested,
        bool onAc,
        int batteryPercent,
        bool expertMode)
    {
        if (!RequiresAcGate(requested)) return requested;
        return AllowsOverclockMode(onAc, batteryPercent, expertMode) ? requested : PowerProfile.Balanced;
    }

    /// <summary>Charge percentage from mWh capacities. Unknown capacity reports 100.</summary>
    public static int BatteryPercent(uint maxCapacity, uint remainingCapacity)
    {
        if (maxCapacity == 0) return 100;
        if (remainingCapacity >= maxCapacity) return 100;

        long percent = remainingCapacity * 100L / maxCapacity;
        return percent < 0 ? 0 : (int)percent;
    }

    /// <summary>
    /// Pure resolver for hardware power mode drift against external writers (NitroSense, BIOS).
    /// When hardware reports a valid mode, hardware truth wins.
    /// When reading fails (null), stored state is preserved.
    /// </summary>
    public static PowerProfile ResolveDrift(PowerProfile? detected, PowerProfile stored) =>
        detected.HasValue ? detected.Value : stored;

    /// <summary>True when hardware reports a valid mode that differs from stored state.</summary>
    public static bool HasDrift(PowerProfile? detected, PowerProfile stored) =>
        detected.HasValue && detected.Value != stored;

    /// <summary>
    /// Computes the next power mode in sequence for the hotkey cycle.
    /// Only cycles through modes supported by hardware.
    /// Skips Performance and Turbo when overclocking is not available (e.g. on battery).
    /// </summary>
    public static PowerProfile GetNextCycleProfile(
        PowerProfile current,
        System.Collections.Generic.IReadOnlyList<PowerProfile> supported,
        bool overclockAvailable)
    {
        if (supported == null || supported.Count == 0)
        {
            supported = [PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance];
        }

        var candidates = new System.Collections.Generic.List<PowerProfile>(supported.Count);
        for (int i = 0; i < supported.Count; i++)
        {
            var p = supported[i];
            if (!overclockAvailable && RequiresAcGate(p)) continue;
            candidates.Add(p);
        }

        if (candidates.Count == 0) return PowerProfile.Balanced;

        int currentIndex = candidates.IndexOf(current);
        if (currentIndex < 0 || currentIndex + 1 >= candidates.Count)
        {
            return candidates[0];
        }

        return candidates[currentIndex + 1];
    }
}
