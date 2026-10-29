using System;
using System.Threading;
using System.Windows;
using Nitrous.Enums;
using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;
using Application = System.Windows.Application;

namespace Nitrous.Tests;

public class DashboardTelemetryCurveCacheTests
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

    [Fact]
    public void DashboardViewModel_FanCurveCaching_ReusesCachedCurvesWhenVersionUnchanged()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            long testVersion = 9876543210L;

            // First load: populates cache
            vm.EnsureCachedCurvesLoaded(testVersion, "Balanced");
            Assert.Equal(testVersion, vm.CachedCurveVersionForTesting);
            var cpuCurveFirst = vm.CachedCpuCurveForTesting;
            var gpuCurveFirst = vm.CachedGpuCurveForTesting;
            Assert.NotNull(cpuCurveFirst);
            Assert.NotNull(gpuCurveFirst);

            // Second load with same version & preset: must reuse identical list instances
            vm.EnsureCachedCurvesLoaded(testVersion, "Balanced");
            Assert.Same(cpuCurveFirst, vm.CachedCpuCurveForTesting);
            Assert.Same(gpuCurveFirst, vm.CachedGpuCurveForTesting);
        });
    }

    [Fact]
    public void DashboardViewModel_FanCurveCaching_InvalidatesOnVersionBump()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            long version1 = 11111L;
            long version2 = 22222L;

            vm.EnsureCachedCurvesLoaded(version1, "Balanced");
            var cpuCurve1 = vm.CachedCpuCurveForTesting;
            Assert.Equal(version1, vm.CachedCurveVersionForTesting);

            // Bump version: cache must invalidate and reload
            vm.EnsureCachedCurvesLoaded(version2, "Balanced");
            Assert.Equal(version2, vm.CachedCurveVersionForTesting);
            Assert.NotSame(cpuCurve1, vm.CachedCpuCurveForTesting);
        });
    }

    [Fact]
    public void DashboardViewModel_FanCurveCaching_InvalidatesOnPowerProfileChange()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            long version = 33333L;

            vm.ActivePowerProfile = PowerProfile.Balanced;
            vm.EnsureCachedCurvesLoaded(version, "Auto");
            var cpuCurveBalanced = vm.CachedCpuCurveForTesting;

            // Change power profile: cache must invalidate and reload profile-specific curve
            vm.ActivePowerProfile = PowerProfile.Turbo;
            vm.EnsureCachedCurvesLoaded(version, "Auto");
            Assert.NotSame(cpuCurveBalanced, vm.CachedCpuCurveForTesting);
        });
    }
}

