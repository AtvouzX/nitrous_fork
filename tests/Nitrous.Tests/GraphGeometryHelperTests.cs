using System;
using System.Collections.Generic;
using Nitrous.Helpers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

public class GraphGeometryHelperTests
{
    [Fact]
    public void GenerateGraphPoints_WhenHistoryIsEmpty_ReturnsEmptyFrozenCollection()
    {
        var points = GraphGeometryHelper.GenerateGraphPoints(Array.Empty<DashboardViewModel.TelemetryPoint>(), true, 1000000);
        Assert.Empty(points);
        Assert.True(points.IsFrozen);
    }

    [Fact]
    public void GenerateGraphFillPoints_WhenHistoryIsEmpty_ReturnsEmptyFrozenCollection()
    {
        var points = GraphGeometryHelper.GenerateGraphFillPoints(Array.Empty<DashboardViewModel.TelemetryPoint>(), true, 1000000);
        Assert.Empty(points);
        Assert.True(points.IsFrozen);
    }

    [Fact]
    public void GenerateGraphPoints_CalculatesCorrectCoordinatesAndClampsValues()
    {
        long now = 300000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = 0, Temp = 0, Usage = 0 },         // Start of window, 0% -> y = 74
            new() { Timestamp = 150000, Temp = 50, Usage = 50 },  // Midpoint -> x = 70.25, y = 37
            new() { Timestamp = 300000, Temp = 120, Usage = 150 } // End of window, clamped to 100 -> x = 140.5, y = 0
        };

        var points = GraphGeometryHelper.GenerateGraphPoints(history, isTemp: true, now: now);

        Assert.Equal(3, points.Count);
        Assert.Equal(0.0, points[0].X, 2);
        Assert.Equal(74.0, points[0].Y, 2);

        Assert.Equal(70.25, points[1].X, 2);
        Assert.Equal(37.0, points[1].Y, 2);

        Assert.Equal(140.5, points[2].X, 2);
        Assert.Equal(0.0, points[2].Y, 2); // Clamped to 100 -> top of canvas
    }

    [Fact]
    public void GenerateGraphFillPoints_IncludesAnchorsAtCanvasBottom()
    {
        long now = 300000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = 0, Temp = 60, Usage = 60 },
            new() { Timestamp = 300000, Temp = 80, Usage = 80 }
        };

        var points = GraphGeometryHelper.GenerateGraphFillPoints(history, isTemp: true, now: now);

        // 2 data points + 2 anchor points (bottom-left and bottom-right) = 4 points total
        Assert.Equal(4, points.Count);

        // Bottom-left anchor
        Assert.Equal(0.0, points[0].X, 2);
        Assert.Equal(74.0, points[0].Y, 2);

        // Data point 1
        Assert.Equal(0.0, points[1].X, 2);

        // Data point 2
        Assert.Equal(140.5, points[2].X, 2);

        // Bottom-right anchor
        Assert.Equal(140.5, points[3].X, 2);
        Assert.Equal(74.0, points[3].Y, 2);
    }

    [Fact]
    public void FindHoverPoint_WhenPointMatchesWithinTolerance_ReturnsSuccess()
    {
        long now = 1000000;
        long targetTime = now - 300000 + 150000; // middle of the 5-min window

        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = targetTime, Temp = 65, Usage = 45 }
        };

        // Middle of canvas (width 200, pixelX 100)
        var result = GraphGeometryHelper.FindHoverPoint(history, pixelX: 100, actualWidth: 200, now: now);

        Assert.True(result.Found);
        Assert.Equal(65, result.Point.Temp);
        Assert.Equal(45, result.Point.Usage);
        Assert.True(result.Diff.TotalMilliseconds >= 0);
    }

    [Fact]
    public void FindHoverPoint_WhenPointOutsideTolerance_ReturnsNotFound()
    {
        long now = 1000000;
        // Point is 30 seconds away from target time (exceeds 10s tolerance)
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = now - 200000, Temp = 65, Usage = 45 }
        };

        var result = GraphGeometryHelper.FindHoverPoint(history, pixelX: 10, actualWidth: 200, now: now);
        Assert.False(result.Found);
    }
}
