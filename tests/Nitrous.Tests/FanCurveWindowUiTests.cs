using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nitrous.Ui;
using Xunit;
using Application = System.Windows.Application;

namespace Nitrous.Tests;

/// <summary>
/// STA UI tests locking the FanCurveWindow Ignite rework:
/// chamfered shell, DashboardStyles token resolution, and shared chamfered tooltips.
/// </summary>
public class FanCurveWindowUiTests
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
                    new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    [Fact]
    public void FanCurveWindow_IgniteRework_HasChamferedShellAndResolvableTokens()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();
            var window = new FanCurveWindow(vm);
            try
            {
                window.Show();

                Assert.NotNull(window.WindowRoot.Clip);
                var clip = Assert.IsType<PathGeometry>(window.WindowRoot.Clip);
                Assert.Single(clip.Figures);
                Assert.True(clip.Figures[0].IsClosed);

                Assert.NotNull(window.WindowShellFrame.Data);
                Assert.IsType<PathGeometry>(window.WindowShellFrame.Data);

                string[] requiredTokens =
                {
                    "Steel950Brush",
                    "Steel900Brush",
                    "Steel850Brush",
                    "Steel700Brush",
                    "Steel600Brush",
                    "Steel400Brush",
                    "Steel100Brush",
                    "Ember500Brush",
                    "Ember400Brush",
                    "MonoFontFamily",
                    "BorderCardBrush",
                    "CompactActionButtonStyle",
                    "SquareCheckBoxStyle",
                    "GhostBtn",
                    "HotkeyTextBoxStyle"
                };

                foreach (var key in requiredTokens)
                {
                    Assert.True(window.TryFindResource(key) != null, $"Expected resource '{key}' to resolve in FanCurveWindow.");
                }

                var tooltipStyle = window.TryFindResource(typeof(System.Windows.Controls.ToolTip));
                Assert.NotNull(tooltipStyle);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FanCurveWindow_IgniteRework_ControlsArePresentAndTooltipsAreMinimal()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();
            var window = new FanCurveWindow(vm);
            try
            {
                // No Show()/window Measure: control presence and tooltips do not need
                // an HWND, and measuring a shown Window can FailFast once the shared
                // test Application has degraded. Construct and assert directly.
                Assert.NotNull(window.ProfileQuiet);
                Assert.NotNull(window.ProfileBalanced);
                Assert.NotNull(window.ProfilePerformance);
                Assert.NotNull(window.TabCpu);
                Assert.NotNull(window.TabGpu);
                Assert.NotNull(window.PresetCombo);
                Assert.NotNull(window.GraphCanvas);
                Assert.NotNull(window.CurveLine);
                Assert.NotNull(window.SaveBtn);
                Assert.NotNull(window.ResetBtn);

                Assert.False(string.IsNullOrWhiteSpace(window.CloseBtn.ToolTip?.ToString()));
                Assert.False(string.IsNullOrWhiteSpace(window.ResetBtn.ToolTip?.ToString()));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
