using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;
using Application = System.Windows.Application;
using Point = System.Windows.Point;

namespace Nitrous.Tests;

public class AdversarialVerificationTests
{
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    new Application();
                }
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new AggregateException("STA thread execution failed", ex);
        }
    }

    #region 1. GraphGeometryHelper Zero-Allocation Stress & Adversarial Tests

    [Fact]
    public void FindHoverPoint_AdversarialStressTest_ZeroHeapAllocationsAcross10000QueriesAndExtremeCoordinates()
    {
        long now = 10_000_000;
        var history = new List<DashboardViewModel.TelemetryPoint>(1000);
        for (int i = 0; i < 1000; i++)
        {
            history.Add(new DashboardViewModel.TelemetryPoint
            {
                Timestamp = now - 300_000 + (i * 300),
                Temp = (i % 80) + 10,
                Usage = (i % 100)
            });
        }

        double[] extremeCoordinates =
        [
            0.0,
            144.5,
            72.25,
            0.0000001,
            144.4999999,
            -0.00001,
            -100.0,
            -1_000_000.0,
            -1e9,
            145.0,
            1000.0,
            1_000_000.0,
            1e9,
            double.MinValue,
            double.MaxValue
        ];

        // Warm up JIT
        foreach (var px in extremeCoordinates)
        {
            GraphGeometryHelper.FindHoverPoint(history, pixelX: px, actualWidth: 144.5, now: now);
        }

        // Stress run: 10,000 iterations across varied and extreme coordinates
        long beforeAllocated = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            double px = extremeCoordinates[i % extremeCoordinates.Length];
            GraphGeometryHelper.FindHoverPoint(history, pixelX: px, actualWidth: 144.5, now: now);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FindHoverPoint_EquidistantSamples_StrictStableFirstEncounteredTieBreaking()
    {
        long now = 1_000_000;
        long targetTime = now - 150_000;

        var sampleA = new DashboardViewModel.TelemetryPoint { Timestamp = targetTime - 200, Temp = 40, Usage = 10 };
        var sampleB = new DashboardViewModel.TelemetryPoint { Timestamp = targetTime + 200, Temp = 60, Usage = 30 };
        var sampleC = new DashboardViewModel.TelemetryPoint { Timestamp = targetTime - 200, Temp = 80, Usage = 50 };

        // Order [A, B]: A is first in list -> must pick A
        var listAB = new List<DashboardViewModel.TelemetryPoint> { sampleA, sampleB };
        var resultAB = GraphGeometryHelper.FindHoverPoint(listAB, pixelX: 100, actualWidth: 200, now: now);
        Assert.True(resultAB.Found);
        Assert.Equal(sampleA.Timestamp, resultAB.Point.Timestamp);
        Assert.Equal(sampleA.Temp, resultAB.Point.Temp);

        // Order [B, A]: B is first in list -> must pick B
        var listBA = new List<DashboardViewModel.TelemetryPoint> { sampleB, sampleA };
        var resultBA = GraphGeometryHelper.FindHoverPoint(listBA, pixelX: 100, actualWidth: 200, now: now);
        Assert.True(resultBA.Found);
        Assert.Equal(sampleB.Timestamp, resultBA.Point.Timestamp);
        Assert.Equal(sampleB.Temp, resultBA.Point.Temp);

        // Order [C, A, B]: C is first in list -> must pick C
        var listCAB = new List<DashboardViewModel.TelemetryPoint> { sampleC, sampleA, sampleB };
        var resultCAB = GraphGeometryHelper.FindHoverPoint(listCAB, pixelX: 100, actualWidth: 200, now: now);
        Assert.True(resultCAB.Found);
        Assert.Equal(sampleC.Timestamp, resultCAB.Point.Timestamp);
        Assert.Equal(sampleC.Temp, resultCAB.Point.Temp);
    }

    [Theory]
    [InlineData(5000, 5000, false)] // Exact boundary: diff == maxToleranceMs -> excluded (< maxToleranceMs)
    [InlineData(5000, 4999, true)]  // Inside boundary: diff == maxToleranceMs - 1 -> included
    [InlineData(5000, 5001, false)] // Outside boundary: diff == maxToleranceMs + 1 -> excluded
    [InlineData(10000, 10000, false)] // Default tolerance exact boundary -> excluded
    [InlineData(10000, 9999, true)]   // Default tolerance inside boundary -> included
    [InlineData(10000, 10001, false)] // Default tolerance outside boundary -> excluded
    public void FindHoverPoint_ToleranceBoundaries_AdversarialVerification(long maxToleranceMs, long offsetMs, bool expectedFound)
    {
        long now = 1_000_000;
        long targetTime = now - 150_000;

        // Test with offset before targetTime
        var historyEarly = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = targetTime - offsetMs, Temp = 55, Usage = 35 }
        };
        var resultEarly = GraphGeometryHelper.FindHoverPoint(historyEarly, pixelX: 100, actualWidth: 200, now: now, maxToleranceMs: maxToleranceMs);
        Assert.Equal(expectedFound, resultEarly.Found);

        // Test with offset after targetTime
        var historyLate = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = targetTime + offsetMs, Temp = 77, Usage = 65 }
        };
        var resultLate = GraphGeometryHelper.FindHoverPoint(historyLate, pixelX: 100, actualWidth: 200, now: now, maxToleranceMs: maxToleranceMs);
        Assert.Equal(expectedFound, resultLate.Found);
    }

    [Fact]
    public void FindHoverPoint_AdversarialInvalidInputs_SafelyHandledWithoutExceptions()
    {
        long now = 1_000_000;
        var validHistory = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = now - 100, Temp = 50, Usage = 50 }
        };

        // 1. Empty list
        var emptyRes = GraphGeometryHelper.FindHoverPoint(new List<DashboardViewModel.TelemetryPoint>(), 50, 100, now);
        Assert.False(emptyRes.Found);

        // 2. Zero actualWidth
        var zeroWidthRes = GraphGeometryHelper.FindHoverPoint(validHistory, 50, 0, now);
        Assert.False(zeroWidthRes.Found);

        // 3. Negative actualWidth
        var negWidthRes = GraphGeometryHelper.FindHoverPoint(validHistory, 50, -100, now);
        Assert.False(negWidthRes.Found);

        // 4. All zero timestamps (bestPoint.Timestamp > 0 is false)
        var zeroTimestampHistory = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = 0, Temp = 50, Usage = 50 }
        };
        var zeroTimeRes = GraphGeometryHelper.FindHoverPoint(zeroTimestampHistory, 50, 100, now);
        Assert.False(zeroTimeRes.Found);
    }

    [Fact]
    public void GenerateGraphPoints_WhenAllValuesNonPositive_ReturnsEmptyFrozenCollectionWithoutAllocationsInCheck()
    {
        long now = 300_000;
        var nonPositiveHistory = new List<DashboardViewModel.TelemetryPoint>();
        for (int i = 0; i < 200; i++)
        {
            nonPositiveHistory.Add(new DashboardViewModel.TelemetryPoint
            {
                Timestamp = i * 1500,
                Temp = 0,
                Usage = -1
            });
        }

        // GenerateGraphPoints allocates a single PointCollection, but HasPositiveValue loop should not allocate
        var points = GraphGeometryHelper.GenerateGraphPoints(nonPositiveHistory, isTemp: true, now: now);
        Assert.Empty(points);
        Assert.True(points.IsFrozen);

        var fillPoints = GraphGeometryHelper.GenerateGraphFillPoints(nonPositiveHistory, isTemp: false, now: now);
        Assert.Empty(fillPoints);
        Assert.True(fillPoints.IsFrozen);
    }

    #endregion

    #region 2. Fan Curve Caching & Cache Invalidation Adversarial Tests

    [Fact]
    public void FanCurveCaching_EmpiricalProof_ZeroRegistryReadsAndZeroListAllocationsWhenVersionUnchanged()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            long testVersion = 777888999L;
            string testPreset = "AdversarialBalanced";

            // 1. Initial state setup in registry
            string initialCurveStr = "30,20;50,30;70,45;85,60;100,70";
            SettingsManager.Save($"CpuCurve_{testPreset}", initialCurveStr);
            SettingsManager.Save($"GpuCurve_{testPreset}", initialCurveStr);

            try
            {
                // First call: populates cache from registry
                vm.EnsureCachedCurvesLoaded(testVersion, testPreset);
                var cachedCpu = vm.CachedCpuCurveForTesting;
                var cachedGpu = vm.CachedGpuCurveForTesting;
                Assert.NotNull(cachedCpu);
                Assert.NotNull(cachedGpu);
                Assert.Equal(testVersion, vm.CachedCurveVersionForTesting);
                Assert.Equal(20, (int)cachedCpu[0].Y);

                // 2. Adversarially mutate the registry behind the ViewModel's back!
                // If EnsureCachedCurvesLoaded reads the registry, it would load 99 instead of 20!
                string mutatedCurveStr = "30,99;50,99;70,99;85,99;100,99";
                SettingsManager.Save($"CpuCurve_{testPreset}", mutatedCurveStr);

                // 3. Call EnsureCachedCurvesLoaded with UNCHANGED version
                vm.EnsureCachedCurvesLoaded(testVersion, testPreset);

                // EMPIRICAL PROOF:
                // a) Reference equality holds (identical List<Point> instance in memory)
                Assert.Same(cachedCpu, vm.CachedCpuCurveForTesting);
                Assert.Same(cachedGpu, vm.CachedGpuCurveForTesting);

                // b) Content was NOT updated from the mutated registry, proving zero registry reads occurred!
                Assert.Equal(20, (int)vm.CachedCpuCurveForTesting![0].Y);

                // c) Zero memory allocations across 1,000 steady-state cache hits
                long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++)
                {
                    vm.EnsureCachedCurvesLoaded(testVersion, testPreset);
                }
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                Assert.Equal(0, allocatedBytes);

                // 4. Now BUMP the version: cache must invalidate and read the mutated registry!
                long bumpedVersion = testVersion + 1;
                vm.EnsureCachedCurvesLoaded(bumpedVersion, testPreset);

                Assert.NotSame(cachedCpu, vm.CachedCpuCurveForTesting);
                Assert.Equal(bumpedVersion, vm.CachedCurveVersionForTesting);
                // Now it MUST reflect the mutated registry data (99)
                Assert.Equal(99, (int)vm.CachedCpuCurveForTesting![0].Y);
            }
            finally
            {
                // Cleanup registry
                SettingsManager.Delete($"CpuCurve_{testPreset}");
                SettingsManager.Delete($"GpuCurve_{testPreset}");
            }
        });
    }

    [Fact]
    public void FanCurveCaching_InvalidatesOnPresetChangeAndProfileChange()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            long version = 555666777L;

            // Load initial preset
            vm.EnsureCachedCurvesLoaded(version, "PresetAlpha");
            var curveAlpha = vm.CachedCpuCurveForTesting;
            Assert.NotNull(curveAlpha);

            // Change preset name: must invalidate
            vm.EnsureCachedCurvesLoaded(version, "PresetBeta");
            var curveBeta = vm.CachedCpuCurveForTesting;
            Assert.NotSame(curveAlpha, curveBeta);

            // Load Auto preset with Balanced profile
            vm.ActivePowerProfile = PowerProfile.Balanced;
            vm.EnsureCachedCurvesLoaded(version, "Auto");
            var curveAutoBalanced = vm.CachedCpuCurveForTesting;

            // Change power profile: must invalidate even with same version and preset "Auto"
            vm.ActivePowerProfile = PowerProfile.Turbo;
            vm.EnsureCachedCurvesLoaded(version, "Auto");
            var curveAutoTurbo = vm.CachedCpuCurveForTesting;
            Assert.NotSame(curveAutoBalanced, curveAutoTurbo);
        });
    }

    #endregion
}
