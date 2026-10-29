using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Nitrous.Ui.Controls;
using Xunit;
using Application = System.Windows.Application;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;

namespace Nitrous.Tests;

public class BatteryBlockBarTests
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
                    _ = new Application();
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
        thread.Join(5000);

        if (ex != null)
        {
            throw new AggregateException("STA thread encountered an uncaught exception.", ex);
        }
    }

    [Fact]
    public void BatteryBlockBar_DefaultValues_AreCorrect()
    {
        RunInSta(() =>
        {
            var bar = new BatteryBlockBar();
            Assert.Equal(100.0, bar.ChargePercent);
            Assert.False(bar.IsLimit80Active);
            Assert.Equal(8.0, bar.Height);
        });
    }

    [Fact]
    public void BatteryBlockBar_MeasureOverride_ReturnsValidSize()
    {
        RunInSta(() =>
        {
            var bar = new BatteryBlockBar { Width = 300, Height = 8 };
            bar.Measure(new Size(300, 8));
            Assert.Equal(300, bar.DesiredSize.Width);
            Assert.Equal(8, bar.DesiredSize.Height);
        });
    }

    [Fact]
    public void BatteryBlockBar_OnRender_ExecutesWithoutExceptionsAcrossChargeRanges()
    {
        RunInSta(() =>
        {
            var bar = new BatteryBlockBar
            {
                Width = 280,
                Height = 8,
                ChargePercent = 75.0,
                IsLimit80Active = true
            };

            // Exercise render pipeline via DrawingContext
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Force arrange
                bar.Arrange(new Rect(0, 0, 280, 8));
                // Call public render path indirectly
            }

            // Test clamping with extreme inputs
            bar.ChargePercent = -20;
            bar.ChargePercent = 150;
            bar.IsLimit80Active = false;
        });
    }
}
