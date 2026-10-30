namespace Nitrous.Helpers;

/// <summary>
/// Pure unit-conversion helpers for power and battery math.
/// Zero hardware, UI, or Win32 dependencies.
/// </summary>
public static class UnitConverter
{
    /// <summary>
    /// Converts milliwatts to watts exactly (no rounding).
    /// </summary>
    public static double MwToW(double milliwatts)
    {
        return milliwatts / 1000.0;
    }

    /// <summary>
    /// Converts milliwatts to watts rounded to 1 decimal for NVML display.
    /// </summary>
    public static double MwToWRounded(double milliwatts)
    {
        return Math.Round(milliwatts / 1000.0, 1);
    }

    /// <summary>
    /// Clamps a percentage to [0, 100].
    /// </summary>
    public static double ClampPercent(double value)
    {
        return Math.Clamp(value, 0.0, 100.0);
    }

    /// <summary>
    /// Battery charge percent from remaining/max capacities. Returns 0 when max is 0.
    /// </summary>
    public static double BatteryPercent(uint remainingCapacity, uint maxCapacity)
    {
        if (maxCapacity == 0) return 0.0;
        return ClampPercent((double)remainingCapacity / maxCapacity * 100.0);
    }

    /// <summary>
    /// Battery charge percent from a 0..1 fraction (e.g. BatteryLifePercent).
    /// </summary>
    public static double BatteryPercentFromFraction(double fraction)
    {
        return ClampPercent(fraction * 100.0);
    }
}
