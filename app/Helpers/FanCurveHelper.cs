using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nitrous.Managers;
using Point = System.Windows.Point;

namespace Nitrous.Helpers;

public static class FanCurveHelper
{
    public static readonly List<Point> DefaultCpuCurve = new()
    {
        new Point(40, 20), new Point(55, 35), new Point(70, 55), new Point(82, 80), new Point(90, 100)
    };

    public static readonly List<Point> DefaultGpuCurve = new()
    {
        new Point(40, 20), new Point(55, 35), new Point(70, 55), new Point(80, 80), new Point(88, 100)
    };

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
