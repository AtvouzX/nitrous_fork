using System;
using System.Collections.Generic;
using System.Linq;
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
    public const double DefaultGraphWidth = 140.5;
    public const double DefaultGraphHeight = 74.0;
    public const long DefaultWindowDurationMs = 300000; // 5 minutes

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
        var points = new PointCollection();
        if (history.Count == 0)
        {
            points.Freeze();
            return points;
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
        var points = new PointCollection();
        if (history.Count == 0)
        {
            points.Freeze();
            return points;
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

        long targetTime = now - windowDurationMs + (long)((pixelX / actualWidth) * windowDurationMs);
        var point = history.OrderBy(p => Math.Abs(p.Timestamp - targetTime)).FirstOrDefault();

        if (point.Timestamp > 0 && Math.Abs(point.Timestamp - targetTime) < maxToleranceMs)
        {
            TimeSpan diff = TimeSpan.FromMilliseconds(now - point.Timestamp);
            return (true, point, diff);
        }

        return (false, default, TimeSpan.Zero);
    }
}
