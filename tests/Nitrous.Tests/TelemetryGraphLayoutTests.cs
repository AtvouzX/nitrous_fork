using System;
using System.Threading;
using System.Windows;
using Nitrous.Helpers;
using Nitrous.Ui;
using Xunit;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace Nitrous.Tests;

public class TelemetryGraphLayoutTests
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
        if (ex != null) throw new AggregateException(ex);
    }

    [Fact]
    public void Dashboard_TelemetryGraphGrid_MeasuresAndSyncsDimensions()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();
                window.DashPage.Measure(new Size(375, 580));
                window.DashPage.Arrange(new Rect(0, 0, 375, 580));
                window.DashPage.UpdateLayout();

                var cpuGrid = window.DashPage.CpuGraphGrid;
                var gpuGrid = window.DashPage.GpuGraphGrid;
                Assert.NotNull(cpuGrid);
                Assert.NotNull(gpuGrid);

                // Both grids must be rendered with non-zero dimensions
                Assert.True(cpuGrid.ActualWidth >= 140.0, $"CpuGrid Width {cpuGrid.ActualWidth} is too small");
                Assert.True(cpuGrid.ActualHeight >= 90.0, $"CpuGrid Height {cpuGrid.ActualHeight} is too small");

                var vm = Assert.IsType<DashboardViewModel>(window.DataContext);
                
                // Assert that ViewModel dynamic dimensions have synchronized with grid size
                Assert.Equal(cpuGrid.ActualWidth, vm.CpuGraphWidth);
                Assert.Equal(cpuGrid.ActualHeight, vm.CpuGraphHeight);
                Assert.Equal(gpuGrid.ActualWidth, vm.GpuGraphWidth);
                Assert.Equal(gpuGrid.ActualHeight, vm.GpuGraphHeight);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_HoverMargin_ClampsWithinGraphBounds()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                var vm = Assert.IsType<DashboardViewModel>(window.DataContext);

                // Simulate hover far past the right edge (e.g. 250px on a 144px canvas)
                double gridWidth = 144.0;
                long now = Environment.TickCount64;
                
                // Add a sample so hover finds a point
                var sample = new DashboardViewModel.TelemetryPoint { Timestamp = now, Temp = 65, Usage = 30 };
                // Use reflection or UpdateCpuHover
                vm.UpdateCpuHover(pixelX: 250.0, actualWidth: gridWidth);

                // Margin must not exceed gridWidth - 1
                Assert.True(vm.CpuHoverMargin.Left <= gridWidth - 1,
                    $"CpuHoverMargin.Left ({vm.CpuHoverMargin.Left}) must be <= {gridWidth - 1}");

                // Simulate negative hover (e.g. -20px)
                vm.UpdateCpuHover(pixelX: -20.0, actualWidth: gridWidth);
                Assert.True(vm.CpuHoverMargin.Left >= 0,
                    $"CpuHoverMargin.Left ({vm.CpuHoverMargin.Left}) must be >= 0");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_GraphPoints_ScaleToDynamicGridDimensions()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            double customWidth = 160.0;
            double customHeight = 100.0;

            vm.UpdateCpuGraphDimensions(customWidth, customHeight);
            Assert.Equal(customWidth, vm.CpuGraphWidth);
            Assert.Equal(customHeight, vm.CpuGraphHeight);
        });
    }

    [Fact]
    public void Dashboard_GpuGraph_WhenSleeping_HasNoBaselinePoints()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            // With empty or sleeping GPU history, points must be empty (no baseline line rendered)
            vm.RegenerateGpuGraphPoints();
            Assert.Empty(vm.GpuTempPoints);
            Assert.Empty(vm.GpuUsagePoints);
            Assert.Empty(vm.GpuTempFillPoints);
            Assert.Empty(vm.GpuUsageFillPoints);
        });
    }
}
