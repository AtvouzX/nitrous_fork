using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nitrous.Helpers;
using Nitrous.Ui.Controls;
using Xunit;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;

namespace Nitrous.Tests;

public class ChamferCardTests
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
    public void ChamferCard_DefaultValues_AreCorrect()
    {
        RunInSta(() =>
        {
            var card = new ChamferCard();
            Assert.Equal(ChamferGeometryHelper.DefaultCardChamfer, card.ChamferSize);
            Assert.NotNull(card.Template);
        });
    }

    [Fact]
    public void ChamferCard_UpdateCardGeometry_GeneratesValidClips()
    {
        RunInSta(() =>
        {
            var card = new ChamferCard
            {
                Width = 200,
                Height = 100,
                ChamferSize = 6.0,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = Brushes.Black
            };

            card.ApplyTemplate();
            card.UpdateCardGeometry();

            Assert.NotNull(card.Clip);
            var clipGeom = Assert.IsType<PathGeometry>(card.Clip);
            Assert.Single(clipGeom.Figures);
            Assert.True(clipGeom.Figures[0].IsClosed);
            Assert.Equal(5, clipGeom.Figures[0].Segments.Count);
        });
    }

    [Fact]
    public void ChamferCard_HandlesZeroOrNegativeDimensionsGracefully()
    {
        RunInSta(() =>
        {
            var card = new ChamferCard
            {
                Width = 0,
                Height = 0
            };

            card.ApplyTemplate();
            // Should not throw
            card.UpdateCardGeometry();
        });
    }

    [Fact]
    public void ChamferCard_PropertyChanges_UpdateFrameStrokes()
    {
        RunInSta(() =>
        {
            var card = new ChamferCard
            {
                Width = 150,
                Height = 80,
                ChamferSize = 4.0
            };

            card.ApplyTemplate();
            card.BorderBrush = Brushes.Red;
            card.Background = Brushes.DarkBlue;
            card.BorderThickness = new Thickness(2);

            var frame = card.Template.FindName("PART_Frame", card) as Path;
            Assert.NotNull(frame);
            Assert.Equal(Brushes.Red, frame.Stroke);
            Assert.Equal(Brushes.DarkBlue, frame.Fill);
            Assert.Equal(2.0, frame.StrokeThickness);
        });
    }

    [Fact]
    public void AllDashboardViews_InstantiateWithChamferCards()
    {
        RunInSta(() =>
        {
            // 1. DashboardPageView
            var dashPage = new Nitrous.Ui.Views.DashboardPageView();
            var dashCards = FindLogicalChildren<ChamferCard>(dashPage).ToList();
            Assert.True(dashCards.Count >= 5, $"Expected at least 5 ChamferCards in DashboardPageView, found {dashCards.Count}");

            // 2. GpuTuningPageView
            var gpuPage = new Nitrous.Ui.Views.GpuTuningPageView();
            var gpuCards = FindLogicalChildren<ChamferCard>(gpuPage).ToList();
            Assert.True(gpuCards.Count >= 2, $"Expected at least 2 ChamferCards in GpuTuningPageView, found {gpuCards.Count}");
            var gpuButtons = FindLogicalChildren<ChamferButton>(gpuPage).ToList();
            Assert.True(gpuButtons.Count >= 2, $"Expected at least 2 ChamferButtons in GpuTuningPageView, found {gpuButtons.Count}");

            // 3. KeyboardPageView
            var kbPage = new Nitrous.Ui.Views.KeyboardPageView();
            var kbCards = FindLogicalChildren<ChamferCard>(kbPage).ToList();
            Assert.True(kbCards.Count >= 2, $"Expected at least 2 ChamferCards in KeyboardPageView, found {kbCards.Count}");

            // 4. SettingsPageView
            var settingsPage = new Nitrous.Ui.Views.SettingsPageView();
            var settingsCards = FindLogicalChildren<ChamferCard>(settingsPage).ToList();
            Assert.True(settingsCards.Count >= 7, $"Expected at least 7 ChamferCards in SettingsPageView, found {settingsCards.Count}");

            var settingsButtons = FindLogicalChildren<ChamferButton>(settingsPage).ToList();
            Assert.True(settingsButtons.Count >= 4, $"Expected at least 4 ChamferButtons in SettingsPageView, found {settingsButtons.Count}");

            var settingsCheckboxes = FindLogicalChildren<System.Windows.Controls.CheckBox>(settingsPage).ToList();
            Assert.True(settingsCheckboxes.Count >= 6, $"Expected at least 6 CheckBoxes in SettingsPageView, found {settingsCheckboxes.Count}");

            var batteryBars = FindLogicalChildren<BatteryBlockBar>(settingsPage).ToList();
            Assert.Single(batteryBars);
        });
    }

    [Fact]
    public void GpuTuningAndKeyboardViews_ControlsAndTooltipsAreValid()
    {
        RunInSta(() =>
        {
            // Verify GPU Tuning Page controls
            var gpuPage = new Nitrous.Ui.Views.GpuTuningPageView();
            Assert.NotNull(gpuPage.GpuCoreSlider);
            Assert.NotNull(gpuPage.GpuMemorySlider);
            Assert.False(string.IsNullOrWhiteSpace(gpuPage.GpuCoreSlider.ToolTip as string));
            Assert.False(string.IsNullOrWhiteSpace(gpuPage.GpuMemorySlider.ToolTip as string));

            var gpuButtons = FindLogicalChildren<ChamferButton>(gpuPage).ToList();
            Assert.True(gpuButtons.Count >= 2);
            var resetBtn = gpuButtons.FirstOrDefault(b => b.Content?.ToString() == "RESET");
            Assert.NotNull(resetBtn);
            Assert.Equal(ChamferButtonVariant.Secondary, resetBtn.Variant);

            // Verify Keyboard Page controls
            var kbPage = new Nitrous.Ui.Views.KeyboardPageView();
            Assert.NotNull(kbPage.TxtHkCycle);
            Assert.NotNull(kbPage.TxtHkDash);
            Assert.False(string.IsNullOrWhiteSpace(kbPage.TxtHkCycle.ToolTip as string));
            Assert.False(string.IsNullOrWhiteSpace(kbPage.TxtHkDash.ToolTip as string));
        });
    }

    private static System.Collections.Generic.IEnumerable<T> FindLogicalChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;
        foreach (object rawChild in LogicalTreeHelper.GetChildren(depObj))
        {
            if (rawChild is DependencyObject child)
            {
                if (child is T typedChild)
                    yield return typedChild;
                foreach (T grandChild in FindLogicalChildren<T>(child))
                    yield return grandChild;
            }
        }
    }
}
