using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Nitrous.Helpers;
using Nitrous.Ui;
using Xunit;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace Nitrous.Tests;

public class DashboardResolutionMatrixTests
{
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                    new Application();
                action();
            }
            catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    [Theory]
    [InlineData(375, 580)] // baseline
    [InlineData(320, 580)] // narrow portrait / snapped
    [InlineData(375, 480)] // short taskbar-crowded
    [InlineData(300, 450)] // 1366x768 overflow probe
    [InlineData(500, 700)] // large external monitor
    public void Dashboard_MeasuresCleanly_AcrossResolutions(double w, double h)
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();
                window.Width = w;
                window.Height = h;
                window.UpdateChamferGeometry();
                Assert.IsType<PathGeometry>(window.WindowRoot.Clip);

                window.DashPage.Measure(new Size(w, h - 86)); // minus title + nav rail
                window.DashPage.Arrange(new Rect(0, 0, w, h - 86));
                window.DashPage.UpdateLayout();

                var cpu = window.DashPage.CpuGraphGrid;
                var gpu = window.DashPage.GpuGraphGrid;
                Assert.True(cpu.ActualWidth > 0, $"CPU grid collapsed at {w}x{h}");
                Assert.True(cpu.ActualHeight > 0, $"CPU grid collapsed at {w}x{h}");
                Assert.True(gpu.ActualWidth > 0, $"GPU grid collapsed at {w}x{h}");

                var vm = Assert.IsType<DashboardViewModel>(window.DataContext);
                foreach (var pts in new[] { vm.CpuTempPoints, vm.CpuUsagePoints, vm.GpuTempPoints, vm.GpuUsagePoints })
                {
                    foreach (var p in pts)
                    {
                        Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), $"NaN point at {w}x{h}");
                        Assert.False(double.IsInfinity(p.X) || double.IsInfinity(p.Y), $"Infinite point at {w}x{h}");
                    }
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Dashboard_SurvivesDpiScale_WithoutLayoutException(double scale)
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();
                window.DashPage.LayoutTransform = new ScaleTransform(scale, scale);
                window.DashPage.Measure(new Size(375 * scale, 580 * scale));
                window.DashPage.Arrange(new Rect(0, 0, 375 * scale, 580 * scale));
                window.DashPage.UpdateLayout();

                Assert.True(window.DashPage.CpuGraphGrid.ActualWidth > 0, $"CPU grid collapsed at {scale * 100}%");
                Assert.True(window.DashPage.GpuGraphGrid.ActualWidth > 0, $"GPU grid collapsed at {scale * 100}%");
            }
            finally
            {
                window.DashPage.LayoutTransform = null;
                window.Close();
            }
        });
    }

    [Fact]
    public void GraphPoints_StayWithinBounds_AtNarrowWidth()
    {
        double w = 100.0, h = 60.0;
        long now = Environment.TickCount64;
        var history = new List<DashboardViewModel.TelemetryPoint>
        {
            new() { Timestamp = now - 60000, Temp = 65, Usage = 30 },
            new() { Timestamp = now, Temp = 70, Usage = 80 },
        };
        var pts = GraphGeometryHelper.GenerateGraphPoints(history, true, now, w, h);
        foreach (var p in pts)
        {
            Assert.InRange(p.X, 0, w);
            Assert.InRange(p.Y, 0, h);
        }
    }
}
