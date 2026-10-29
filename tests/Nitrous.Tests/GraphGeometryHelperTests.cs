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
            new() { Timestamp = 0, Temp = 0, Usage = 0 },         // Start of window, 0% -> y = 94
            new() { Timestamp = 150000, Temp = 50, Usage = 50 },  // Midpoint -> x = 72.25, y = 47
            new() { Timestamp = 300000, Temp = 120, Usage = 150 } // End of window, clamped to 100 -> x = 144.5, y = 0
        };

        var points = GraphGeometryHelper.GenerateGraphPoints(history, isTemp: true, now: now);

        Assert.Equal(3, points.Count);
        Assert.Equal(0.0, points[0].X, 2);
        Assert.Equal(94.0, points[0].Y, 2);

        Assert.Equal(72.25, points[1].X, 2);
        Assert.Equal(47.0, points[1].Y, 2);

        Assert.Equal(144.5, points[2].X, 2);
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
        Assert.Equal(94.0, points[0].Y, 2);

        // Data point 1
        Assert.Equal(0.0, points[1].X, 2);

        // Data point 2
        Assert.Equal(144.5, points[2].X, 2);

        // Bottom-right anchor
        Assert.Equal(144.5, points[3].X, 2);
        Assert.Equal(94.0, points[3].Y, 2);
    }

    [Fact]
    public void GenerateGraphPoints_WithCustomDimensions_ScalesAccurately()
    {
        long now = 300000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = 0, Temp = 0, Usage = 0 },
            new() { Timestamp = 300000, Temp = 100, Usage = 100 }
        };

        var points = GraphGeometryHelper.GenerateGraphPoints(history, isTemp: true, now: now, graphWidth: 200, graphHeight: 120);

        Assert.Equal(2, points.Count);
        Assert.Equal(0.0, points[0].X, 2);
        Assert.Equal(120.0, points[0].Y, 2);
        Assert.Equal(200.0, points[1].X, 2);
        Assert.Equal(0.0, points[1].Y, 2);
    }

    [Fact]
    public void FindHoverPoint_WhenPixelXExceedsActualWidth_ClampsToActualWidth()
    {
        long now = 1000000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = now, Temp = 70, Usage = 50 }
        };

        // pixelX is 250 on a 200 width canvas -> should clamp to now
        var result = GraphGeometryHelper.FindHoverPoint(history, pixelX: 250, actualWidth: 200, now: now);
        Assert.True(result.Found);
        Assert.Equal(70, result.Point.Temp);
        Assert.Equal(50, result.Point.Usage);
    }

    [Fact]
    public void FindHoverPoint_WhenPixelXNegative_ClampsToZero()
    {
        long now = 1000000;
        long windowStart = now - 300000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = windowStart, Temp = 45, Usage = 20 }
        };

        // pixelX is -50 on a 200 width canvas -> should clamp to windowStart
        var result = GraphGeometryHelper.FindHoverPoint(history, pixelX: -50, actualWidth: 200, now: now);
        Assert.True(result.Found);
        Assert.Equal(45, result.Point.Temp);
        Assert.Equal(20, result.Point.Usage);
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

    [Fact]
    public void GenerateGraphPoints_WhenAllValuesZeroOrNegative_ReturnsEmptyToPreventBaselineArtifact()
    {
        long now = 300000;
        var sleepingHistory = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = 0, Temp = 0, Usage = 0 },
            new() { Timestamp = 150000, Temp = 0, Usage = 0 },
            new() { Timestamp = 300000, Temp = 0, Usage = 0 }
        };

        var tempPoints = GraphGeometryHelper.GenerateGraphPoints(sleepingHistory, isTemp: true, now: now);
        var usagePoints = GraphGeometryHelper.GenerateGraphPoints(sleepingHistory, isTemp: false, now: now);
        var tempFillPoints = GraphGeometryHelper.GenerateGraphFillPoints(sleepingHistory, isTemp: true, now: now);
        var usageFillPoints = GraphGeometryHelper.GenerateGraphFillPoints(sleepingHistory, isTemp: false, now: now);

        Assert.Empty(tempPoints);
        Assert.Empty(usagePoints);
        Assert.Empty(tempFillPoints);
        Assert.Empty(usageFillPoints);
    }

    [Fact]
    public void FindHoverPoint_ZeroAllocationsOnHeap_DoesNotAllocateDuringHoverScan()
    {
        long now = 1000000;
        var history = new List<DashboardViewModel.TelemetryPoint>();
        for (int i = 0; i < 200; i++)
        {
            history.Add(new DashboardViewModel.TelemetryPoint
            {
                Timestamp = now - 300000 + (i * 1500),
                Temp = 40 + (i % 30),
                Usage = 20 + (i % 60)
            });
        }

        // Warm up JIT and method compilation
        GraphGeometryHelper.FindHoverPoint(history, pixelX: 72.0, actualWidth: 144.5, now: now);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 500; i++)
        {
            double px = (i % 144);
            GraphGeometryHelper.FindHoverPoint(history, pixelX: px, actualWidth: 144.5, now: now);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FindHoverPoint_WithEqualDeltaPoints_PreservesFirstEncounteredPoint()
    {
        long now = 1000000;
        long targetTime = now - 150000;

        // Two points exactly 100ms before and 100ms after targetTime
        var pointEarly = new DashboardViewModel.TelemetryPoint { Timestamp = targetTime - 100, Temp = 50, Usage = 30 };
        var pointLate = new DashboardViewModel.TelemetryPoint { Timestamp = targetTime + 100, Temp = 75, Usage = 60 };

        var history = new List<DashboardViewModel.TelemetryPoint> { pointEarly, pointLate };

        // pixelX at exact midpoint of 300s window on 200px width -> targetTime = now - 150000
        var result = GraphGeometryHelper.FindHoverPoint(history, pixelX: 100, actualWidth: 200, now: now);

        Assert.True(result.Found);
        Assert.Equal(pointEarly.Temp, result.Point.Temp);
        Assert.Equal(pointEarly.Timestamp, result.Point.Timestamp);
    }

    [Fact]
    public void FindHoverPoint_AtExactToleranceThreshold_ExcludesSample()
    {
        long now = 1000000;
        long targetTime = now - 150000;
        long maxTol = 5000;

        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = targetTime - maxTol, Temp = 60, Usage = 40 }
        };

        var result = GraphGeometryHelper.FindHoverPoint(history, pixelX: 100, actualWidth: 200, now: now, maxToleranceMs: maxTol);
        Assert.False(result.Found);
    }

    [Fact]
    public void FindHoverPoint_AtToleranceMinusOne_IncludesSample()
    {
        long now = 1000000;
        long targetTime = now - 150000;
        long maxTol = 5000;

        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = targetTime - maxTol + 1, Temp = 60, Usage = 40 }
        };

        var result = GraphGeometryHelper.FindHoverPoint(history, pixelX: 100, actualWidth: 200, now: now, maxToleranceMs: maxTol);
        Assert.True(result.Found);
        Assert.Equal(60, result.Point.Temp);
    }

    [Fact]
    public void FindHoverPoint_WithZeroOrNegativeCanvasWidth_ReturnsNotFound()
    {
        long now = 1000000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = now, Temp = 55, Usage = 35 }
        };

        var zeroWidth = GraphGeometryHelper.FindHoverPoint(history, pixelX: 10, actualWidth: 0, now: now);
        var negativeWidth = GraphGeometryHelper.FindHoverPoint(history, pixelX: 10, actualWidth: -100, now: now);

        Assert.False(zeroWidth.Found);
        Assert.False(negativeWidth.Found);
    }

    [Fact]
    public void GenerateGraphPoints_WhenFirstPositiveThenZero_GeneratesPoints()
    {
        long now = 300000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = 0, Temp = 45, Usage = 20 },
            new() { Timestamp = 150000, Temp = 0, Usage = 0 }
        };

        var points = GraphGeometryHelper.GenerateGraphPoints(history, isTemp: true, now: now);
        Assert.Equal(2, points.Count);
    }

    [Fact]
    public void GenerateGraphPoints_WhenNegativeValuesOnly_ReturnsEmptyCollection()
    {
        long now = 300000;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = 0, Temp = -10, Usage = -5 },
            new() { Timestamp = 150000, Temp = 0, Usage = 0 }
        };

        var points = GraphGeometryHelper.GenerateGraphPoints(history, isTemp: true, now: now);
        Assert.Empty(points);
        Assert.True(points.IsFrozen);
    }
}
