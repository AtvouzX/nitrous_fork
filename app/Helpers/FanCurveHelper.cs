using Nitrous.Managers;
using Nitrous.Enums;
using Point = System.Windows.Point;

namespace Nitrous.Helpers;

public static class FanCurveHelper
{
    public const double MinRpm = 1800.0;
    public const double MaxRpm = 5850.0;
    public const int DefaultMaxRpm = 5850;
    public const int MaxPlausibleRpm = 12000;
    public const double MinDutyPercentage = 30.0;
    public const double MaxDutyPercentage = 100.0;
    public const string MaxCpuRpmKey = "MaxCpuRpm";
    public const string MaxGpuRpmKey = "MaxGpuRpm";

    /// <summary>
    /// Converts physical fan tachometer RPM into an estimated duty-cycle percentage (30% - 100%)
    /// using the provided or default ceiling RPM.
    /// Returns 0 if RPM is zero or negative (fan stopped/idle).
    /// </summary>
    public static int RpmToPercentage(int rpm, double maxRpm = MaxRpm)
    {
        if (rpm <= 0) return 0;
        double effectiveMax = Math.Max(maxRpm, MinRpm + 1.0);
        return Math.Clamp(
            (int)Math.Round((rpm - MinRpm) / (effectiveMax - MinRpm) * (MaxDutyPercentage - MinDutyPercentage) +
                            MinDutyPercentage), 0, 100);
    }

    /// <summary>
    /// Pure helper that evaluates whether a newly observed tachometer RPM beats the current calibrated ceiling
    /// while remaining within physically plausible hardware bounds.
    /// </summary>
    public static int TryCalibrateCeiling(int observedRpm, int currentCeiling, out bool updated)
    {
        int baseline = currentCeiling >= DefaultMaxRpm && currentCeiling <= MaxPlausibleRpm
            ? currentCeiling
            : DefaultMaxRpm;

        if (observedRpm > baseline && observedRpm <= MaxPlausibleRpm)
        {
            updated = true;
            return observedRpm;
        }

        updated = false;
        return baseline;
    }

    /// <summary>
    /// Reads the persisted fan RPM ceiling from settings, updates and saves it if the observed RPM
    /// establishes a new valid high-water mark, and returns the active ceiling.
    /// </summary>
    public static int ObserveAndGetCalibratedMaxRpm(string settingKey, int observedRpm)
    {
        int currentCeiling = SettingsManager.GetInt(settingKey, DefaultMaxRpm);
        int newCeiling = TryCalibrateCeiling(observedRpm, currentCeiling, out bool updated);
        if (updated)
        {
            SettingsManager.Save(settingKey, newCeiling);
            LogManager.Info($"Calibrated new fan RPM ceiling for '{settingKey}': {newCeiling} RPM", "FanCurve");
        }
        return newCeiling;
    }

    public static List<Point> GetDefaultCpuCurve(PowerProfile profile)
    {
        return profile switch
        {
            PowerProfile.Eco or PowerProfile.Quiet => [new(30, 20), new(50, 30), new(70, 45), new(85, 60), new(100, 70)],
            PowerProfile.Performance => [new(30, 40), new(45, 50), new(60, 70), new(80, 90), new(100, 100)],
            PowerProfile.Turbo => [new(30, 50), new(45, 65), new(60, 80), new(75, 100), new(100, 100)],
            _ => [new(30, 20), new(45, 35), new(60, 50), new(75, 70), new(100, 100)] // Balanced
        };
    }

    public static List<Point> GetDefaultGpuCurve(PowerProfile profile)
    {
        return profile switch
        {
            PowerProfile.Eco or PowerProfile.Quiet => [new(30, 20), new(50, 35), new(70, 50), new(85, 65), new(100, 75)],
            PowerProfile.Performance => [new(30, 40), new(45, 55), new(60, 75), new(80, 95), new(100, 100)],
            PowerProfile.Turbo => [new(30, 50), new(45, 70), new(60, 85), new(75, 100), new(100, 100)],
            _ => [new(30, 20), new(45, 40), new(60, 55), new(75, 75), new(100, 100)] // Balanced
        };
    }

    public static int InterpolateSpeed(IReadOnlyList<Point>? curve, int currentTemp)
    {
        if (curve == null || curve.Count == 0) return 50;

        if (currentTemp <= curve[0].X) return (int)curve[0].Y;
        if (currentTemp >= curve[^1].X) return (int)curve[^1].Y;

        for (int i = 0; i < curve.Count - 1; i++)
        {
            if (currentTemp >= curve[i].X && currentTemp <= curve[i + 1].X)
            {
                double span = curve[i + 1].X - curve[i].X;
                if (span == 0) return (int)curve[i].Y;

                double t = (currentTemp - curve[i].X) / span;
                return (int)Math.Round(curve[i].Y + t * (curve[i + 1].Y - curve[i].Y));
            }
        }

        return (int)curve[^1].Y;
    }

    public static int GetClampedSpeed(IReadOnlyList<Point>? curve, int currentTemp)
    {
        return Math.Clamp(InterpolateSpeed(curve, currentTemp), 0, 100);
    }

    public static void SaveCurveToRegistry(string keyName, List<Point> points)
    {
        // Ensure points are sorted by temperature (X) before saving
        var sorted = points.OrderBy(p => p.X);
        string data = string.Join(";", sorted.Select(p => $"{(int)p.X},{(int)p.Y}"));
        SettingsManager.Save(keyName, data);
    }

    public static List<Point> LoadCurveFromRegistry(string keyName, List<Point> defaultCurve)
    {
        string data = SettingsManager.Get(keyName, "");
        if (string.IsNullOrEmpty(data)) return [.. defaultCurve];

        try
        {
            var pts = new List<Point>();
            foreach (var pair in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                {
                    pts.Add(new Point(x, y));
                }
            }

            if (pts.Count >= 2)
            {
                pts.Sort((a, b) => a.X.CompareTo(b.X));
                return pts;
            }

            return [.. defaultCurve];
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Failed parsing fan curve from registry '{keyName}': {ex.Message}", "FanCurve");
            return [.. defaultCurve];
        }
    }
}
