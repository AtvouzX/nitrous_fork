using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nitrous.Managers;
using Nitrous.Enums;

using Point = System.Windows.Point;

namespace Nitrous.Helpers;

public static class FanCurveHelper
{
    public static List<Point> GetDefaultCpuCurve(PowerProfile profile)
    {
        return profile switch
        {
            PowerProfile.Quiet => new List<Point> { new(30, 20), new(50, 30), new(70, 45), new(85, 60), new(100, 70) },
            PowerProfile.Performance => new List<Point> { new(30, 40), new(45, 50), new(60, 70), new(80, 90), new(100, 100) },
            PowerProfile.Turbo => new List<Point> { new(30, 50), new(45, 65), new(60, 80), new(75, 100), new(100, 100) },
            _ => new List<Point> { new(30, 20), new(45, 35), new(60, 50), new(75, 70), new(100, 100) } // Balanced
        };
    }

    public static List<Point> GetDefaultGpuCurve(PowerProfile profile)
    {
        return profile switch
        {
            PowerProfile.Quiet => new List<Point> { new(30, 20), new(50, 35), new(70, 50), new(85, 65), new(100, 75) },
            PowerProfile.Performance => new List<Point> { new(30, 40), new(45, 55), new(60, 75), new(80, 95), new(100, 100) },
            PowerProfile.Turbo => new List<Point> { new(30, 50), new(45, 70), new(60, 85), new(75, 100), new(100, 100) },
            _ => new List<Point> { new(30, 20), new(45, 40), new(60, 55), new(75, 75), new(100, 100) } // Balanced
        };
    }

    public static int InterpolateSpeed(List<Point> curve, int currentTemp)
    {
        if (curve == null || curve.Count == 0) return 50;

        var sorted = curve.OrderBy(p => p.X).ToList();
        if (currentTemp <= sorted[0].X) return (int)sorted[0].Y;
        if (currentTemp >= sorted[^1].X) return (int)sorted[^1].Y;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (currentTemp >= sorted[i].X && currentTemp <= sorted[i + 1].X)
            {
                double span = sorted[i + 1].X - sorted[i].X;
                if (span == 0) return (int)sorted[i].Y;

                double t = (currentTemp - sorted[i].X) / span;
                return (int)Math.Round(sorted[i].Y + t * (sorted[i + 1].Y - sorted[i].Y));
            }
        }
        return (int)sorted[^1].Y;
    }

    public static void SaveCurveToRegistry(string keyName, List<Point> points)
    {
        // Convert points to a semicolon-separated string: "X,Y;X,Y;X,Y"
        string data = string.Join(";", points.Select(p => $"{(int)p.X},{(int)p.Y}"));
        SettingsManager.Save(keyName, data);
    }

    public static List<Point> LoadCurveFromRegistry(string keyName, List<Point> defaultCurve)
    {
        string data = SettingsManager.Get(keyName, "");
        if (string.IsNullOrEmpty(data)) return new List<Point>(defaultCurve);

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
            return pts.Count >= 2 ? pts : new List<Point>(defaultCurve);
        }
        catch
        {
            return new List<Point>(defaultCurve);
        }
    }
}
