using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nitrous.Ui;
using Point = System.Windows.Point;

namespace Nitrous.Helpers;

/// <summary>
/// Pure mathematical helper to compute WPF PointCollection geometry for live telemetry graphs.
/// </summary>
public static class GraphGeometryHelper
{
    public const double DefaultGraphWidth = 144.5;
    public const double DefaultGraphHeight = 94.0;
    public const long DefaultWindowDurationMs = 300000; // 5 minutes

    private static readonly PointCollection SharedEmptyPoints = CreateSharedEmpty();

    private static PointCollection CreateSharedEmpty()
    {
        var empty = new PointCollection();
        empty.Freeze();
        return empty;
    }

    private static bool HasPositiveValue(IReadOnlyList<DashboardViewModel.TelemetryPoint> history, bool isTemp)
    {
        for (int i = 0; i < history.Count; i++)
        {
            if ((isTemp ? history[i].Temp : history[i].Usage) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Generates stroke line points for a telemetry history stream.
    /// </summary>
    public static PointCollection GenerateGraphPoints(
        IReadOnlyList<DashboardViewModel.TelemetryPoint> history,
        bool isTemp,
        long now,
        double graphWidth = DefaultGraphWidth,
        double graphHeight = DefaultGraphHeight,
        long windowDurationMs = DefaultWindowDurationMs)
    {
        var points = new PointCollection(history.Count);
        if (history.Count == 0 || !HasPositiveValue(history, isTemp))
        {
            return SharedEmptyPoints;
        }

        long windowStart = now - windowDurationMs;

        for (int i = 0; i < history.Count; i++)
        {
            var p = history[i];
            double x = ((p.Timestamp - windowStart) / (double)windowDurationMs) * graphWidth;
            double val = Math.Clamp(isTemp ? p.Temp : p.Usage, 0, 100);
            double y = ((100 - val) / 100.0) * graphHeight;
            points.Add(new Point(x, y));
        }

        points.Freeze();
        return points;
    }

    /// <summary>
    /// Generates polygon fill points for a telemetry history stream (clamped to graph bottom).
    /// </summary>
    public static PointCollection GenerateGraphFillPoints(
        IReadOnlyList<DashboardViewModel.TelemetryPoint> history,
        bool isTemp,
        long now,
        double graphWidth = DefaultGraphWidth,
        double graphHeight = DefaultGraphHeight,
        long windowDurationMs = DefaultWindowDurationMs)
    {
        var points = new PointCollection(history.Count + 2);
        if (history.Count == 0 || !HasPositiveValue(history, isTemp))
        {
            return SharedEmptyPoints;
        }

        long windowStart = now - windowDurationMs;

        // Bottom left anchor
        double firstX = ((history[0].Timestamp - windowStart) / (double)windowDurationMs) * graphWidth;
        points.Add(new Point(firstX, graphHeight));

        for (int i = 0; i < history.Count; i++)
        {
            var p = history[i];
            double x = ((p.Timestamp - windowStart) / (double)windowDurationMs) * graphWidth;
            double val = Math.Clamp(isTemp ? p.Temp : p.Usage, 0, 100);
            double y = ((100 - val) / 100.0) * graphHeight;
            points.Add(new Point(x, y));
        }

        // Bottom right anchor
        double lastX = ((history[^1].Timestamp - windowStart) / (double)windowDurationMs) * graphWidth;
        points.Add(new Point(lastX, graphHeight));

        points.Freeze();
        return points;
    }

    /// <summary>
    /// Locates the closest telemetry sample to the mouse cursor X coordinate.
    /// </summary>
    public static (bool Found, DashboardViewModel.TelemetryPoint Point, TimeSpan Diff) FindHoverPoint(
        IReadOnlyList<DashboardViewModel.TelemetryPoint> history,
        double pixelX,
        double actualWidth,
        long now,
        long windowDurationMs = DefaultWindowDurationMs,
        long maxToleranceMs = 10000)
    {
        if (history.Count == 0 || actualWidth <= 0)
        {
            return (false, default, TimeSpan.Zero);
        }

        double clampedX = Math.Clamp(pixelX, 0, actualWidth);
        long targetTime = now - windowDurationMs + (long)((clampedX / actualWidth) * windowDurationMs);

        DashboardViewModel.TelemetryPoint bestPoint = default;
        long minDiff = long.MaxValue;

        for (int i = 0; i < history.Count; i++)
        {
            var p = history[i];
            long diff = Math.Abs(p.Timestamp - targetTime);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestPoint = p;
            }
        }

        if (bestPoint.Timestamp > 0 && minDiff < maxToleranceMs)
        {
            TimeSpan diff = TimeSpan.FromMilliseconds(now - bestPoint.Timestamp);
            return (true, bestPoint, diff);
        }

        return (false, default, TimeSpan.Zero);
    }
}
