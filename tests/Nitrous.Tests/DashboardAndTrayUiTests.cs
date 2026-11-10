using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Nitrous.Enums;
using Nitrous.Interop;
using Nitrous.Managers;
using Nitrous.Ui;
using Nitrous.Ui.Views;
using Xunit;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;

namespace Nitrous.Tests;

public class DashboardAndTrayUiTests
{
    /// <summary>
    /// Executes test logic within a dedicated Single-Threaded Apartment (STA) thread,
    /// required for creating and manipulating WPF Windows and WinForms UI controls.
    /// </summary>
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Ensure WPF Application context exists so pack:// URI schemes resolve properly.
                // OnExplicitShutdown: closing one test window must not shut down the
                // shared Application and break later Show()/layout in other tests.
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

    // =========================================================================
    // 1. NitrousDashboard WPF UI Tests
    // =========================================================================

    [Fact]
    public void Dashboard_PageViews_AreInstantiatedAsDedicatedComponents()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                Assert.IsAssignableFrom<FrameworkElement>(window.DashPage);
                Assert.IsAssignableFrom<FrameworkElement>(window.GpuPage);
                Assert.IsAssignableFrom<FrameworkElement>(window.SettingsPage);
                Assert.IsAssignableFrom<FrameworkElement>(window.KeyboardPage);

                Assert.Equal("KeyboardPageView", window.KeyboardPage.GetType().Name);
                Assert.Equal("GpuTuningPageView", window.GpuPage.GetType().Name);
                Assert.Equal("SettingsPageView", window.SettingsPage.GetType().Name);
                Assert.Equal("DashboardPageView", window.DashPage.GetType().Name);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DashboardStyles_AllSharedStyles_AreCentralizedInResourceDictionaryAndResolvable()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                // Verify merged dictionary contains DashboardStyles
                Assert.True(window.Resources.MergedDictionaries.Count > 0, "NitrousDashboard must include MergedDictionaries.");

                // Assert that all core styles are cleanly resolvable from the centralized dictionary
                string[] requiredStyleKeys =
                {
                    "CachedAppLogo",
                    "ActionButtonStyle",
                    "ModernToggleStyle",
                    "ChamferMiniToggleStyle",
                    "ModernSliderStyle",
                    "SquareCheckBoxStyle",
                    "NavButtonStyle",
                    "GhostBtn",
                    "AccentBtn",
                    "VerticalDetentButtonStyle",
                    "CompactActionButtonStyle",
                    "IgniteProgressBarStyle",
                    "SmallGhostToggleBtn",
                    "HotkeyTextBoxStyle",
                    "BoolToVis"
                };

                foreach (var key in requiredStyleKeys)
                {
                    var resource = window.TryFindResource(key);
                    Assert.True(resource != null, $"Expected resource '{key}' to be defined and resolvable in DashboardStyles.");
                }

                // Verify ToolTip default style
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
    public void Dashboard_InitializesWithCorrectVersionAndActiveDashboardPage()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                // Initial page visibility: Dashboard visible, other tabs collapsed
                Assert.Equal(Visibility.Visible, window.DashPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.GpuPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.SettingsPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.KeyboardPage.Visibility);

                // DataContext is properly wired to ViewModel
                Assert.NotNull(window.DataContext);
                Assert.IsType<DashboardViewModel>(window.DataContext);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_PageNavigation_SwitchesVisibilityAccurately()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                // Navigate to GPU
                window.NavigateToGpu();
                Assert.Equal(Visibility.Visible, window.GpuPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.DashPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.SettingsPage.Visibility);

                // Navigate to Settings
                window.NavigateToSettings();
                Assert.Equal(Visibility.Visible, window.SettingsPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.GpuPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.DashPage.Visibility);

                // Navigate to Keyboard
                window.NavigateToKeyboard();
                Assert.Equal(Visibility.Visible, window.KeyboardPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.SettingsPage.Visibility);

                // Navigate back to Dashboard
                window.NavigateToDashboard();
                Assert.Equal(Visibility.Visible, window.DashPage.Visibility);
                Assert.Equal(Visibility.Collapsed, window.KeyboardPage.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_RefreshDashboardState_PopulatesPillAndProfileRadioButtons()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.RefreshDashboardState();

                // Check binding exists instead of literal text because bindings are async
                var vm = window.DataContext as DashboardViewModel;
                Assert.NotNull(vm);
                Assert.True(vm.BatteryBadgeText == "AC POWER" || vm.BatteryBadgeText == "CHARGED" ||
                            vm.BatteryBadgeText.StartsWith("BATTERY") || vm.BatteryBadgeText.StartsWith("CHARGING"));

                // Power Profile selection: exactly one button must be active
                bool powerActive = window.BtnPowerQuiet.IsChecked == true ||
                                   window.BtnPowerBal.IsChecked == true ||
                                   window.BtnPowerPerf.IsChecked == true ||
                                   window.BtnPowerTurbo.IsChecked == true;
                Assert.True(powerActive, "Expected at least one Power Profile radio button to be checked.");

                // Fan Profile selection: exactly one button must be active
                bool fanActive = window.BtnFanAuto.IsChecked == true ||
                                 window.BtnFanMax.IsChecked == true ||
                                 window.BtnFanCustom.IsChecked == true;
                Assert.True(fanActive, "Expected at least one Fan Profile radio button to be checked.");

                // Refresh Profile selection: exactly one button must be active
                bool refreshActive = window.BtnRefreshAuto.IsChecked == true ||
                                     window.BtnRefresh60.IsChecked == true ||
                                     window.BtnRefreshMax.IsChecked == true;
                Assert.True(refreshActive, "Expected at least one Refresh Profile radio button to be checked.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_RefreshBadgeAndSubtext_FormatsCorrectly()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            vm.ActiveRefreshProfile = RefreshProfile.Auto;
            Assert.Equal("AUTO", vm.RefreshBadgeText);
            Assert.StartsWith("60/", vm.AutoRefreshSubtext);

            vm.ActiveRefreshProfile = RefreshProfile.Hz60;
            Assert.Equal("60HZ", vm.RefreshBadgeText);

            vm.ActiveRefreshProfile = RefreshProfile.MaxHz;
            Assert.Equal(vm.MaxRefreshText, vm.RefreshBadgeText);
        });
    }

    [Fact]
    public void DashboardViewModel_FanViewsAndPercentage_ToggleVisibilityAndFormatCorrectly()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();

            // When in Auto or Max: Read View should be Visible, Write View should be Collapsed
            vm.IsCustomFanEnabled = false;
            Assert.Equal(System.Windows.Visibility.Visible, vm.FanReadViewVisibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, vm.FanWriteViewVisibility);

            // When Custom fan enabled with curve: Read view should still be Visible
            vm.IsCustomFanEnabled = true;
            vm.IsCurveModeEnabled = true;
            Assert.Equal(System.Windows.Visibility.Visible, vm.FanReadViewVisibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, vm.FanWriteViewVisibility);

            // When Custom fan enabled with fixed sliders: Write view should be Visible
            vm.IsCurveModeEnabled = false;
            Assert.Equal(System.Windows.Visibility.Collapsed, vm.FanReadViewVisibility);
            Assert.Equal(System.Windows.Visibility.Visible, vm.FanWriteViewVisibility);

            // Fan percentage format strings
            vm.CpuFanPercentage = 65;
            vm.GpuFanPercentage = 70;
            Assert.Equal("65%", vm.CpuFanPctText);
            Assert.Equal("70%", vm.GpuFanPctText);
        });
    }

    [Fact]
    public void DashboardViewModel_SetRefreshCommand_UpdatesActiveRefreshProfile()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();

            vm.SetRefreshCommand.Execute("Hz60");
            Assert.Equal(RefreshProfile.Hz60, vm.ActiveRefreshProfile);
            Assert.Equal("60HZ", vm.RefreshBadgeText);

            vm.SetRefreshCommand.Execute("Auto");
            Assert.Equal(RefreshProfile.Auto, vm.ActiveRefreshProfile);
            Assert.Equal("AUTO", vm.RefreshBadgeText);

            vm.SetRefreshCommand.Execute("MaxHz");
            Assert.Equal(RefreshProfile.MaxHz, vm.ActiveRefreshProfile);
            Assert.Equal(vm.MaxRefreshText, vm.RefreshBadgeText);
        });
    }

    [Fact]
    public void DashboardViewModel_UpdateBatteryBadge_UpdatesPropertiesCorrectly()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();

            // 1. AC Power, battery full / not charging
            var acState = new Powrprof.SYSTEM_BATTERY_STATE
            {
                BatteryPresent = true,
                AcOnLine = true,
                Charging = false,
                Rate = 0
            };
            vm.UpdateBatteryBadge(acState);
            Assert.Equal("CHARGED", vm.BatteryBadgeText);
            Assert.Equal("#EDEDEF", vm.BatteryBadgeForeground);
            Assert.Equal("#121215", vm.BatteryBadgeBackground);
            Assert.Equal("#26262B", vm.BatteryBadgeBorderBrush);

            // 2. AC Power & Charging with wattage
            var chargingState = new Powrprof.SYSTEM_BATTERY_STATE
            {
                BatteryPresent = true,
                AcOnLine = true,
                Charging = true,
                Rate = 45000 // 45W
            };
            vm.UpdateBatteryBadge(chargingState);
            Assert.Equal("CHARGING • 45.0W", vm.BatteryBadgeText);
            Assert.Equal("#E85D2A", vm.BatteryBadgeForeground);
            Assert.Equal("#121215", vm.BatteryBadgeBackground);
            Assert.Equal("#26262B", vm.BatteryBadgeBorderBrush);

            // 3. Battery Power with discharge rate
            var batteryState = new Powrprof.SYSTEM_BATTERY_STATE
            {
                BatteryPresent = true,
                AcOnLine = false,
                Discharging = true,
                Rate = -20500 // -20.5W
            };
            vm.UpdateBatteryBadge(batteryState);
            Assert.Equal("BATTERY • 20.5W", vm.BatteryBadgeText);
            Assert.Equal("#EDEDEF", vm.BatteryBadgeForeground);
            Assert.Equal("#18181C", vm.BatteryBadgeBackground);
            Assert.Equal("#26262B", vm.BatteryBadgeBorderBrush);

            // 4. Battery Power with 0 rate
            var batteryNoRate = new Powrprof.SYSTEM_BATTERY_STATE
            {
                BatteryPresent = true,
                AcOnLine = false,
                Rate = 0
            };
            vm.UpdateBatteryBadge(batteryNoRate);
            Assert.Equal("BATTERY", vm.BatteryBadgeText);
            Assert.Equal("#18181C", vm.BatteryBadgeBackground);
            Assert.Equal("#26262B", vm.BatteryBadgeBorderBrush);

            // 5. Desktop / No battery present
            var desktopState = new Powrprof.SYSTEM_BATTERY_STATE
            {
                BatteryPresent = false,
                AcOnLine = false
            };
            vm.UpdateBatteryBadge(desktopState);
            Assert.Equal("AC POWER", vm.BatteryBadgeText);
            Assert.Equal("#8A8A96", vm.BatteryBadgeForeground);
            Assert.Equal("#121215", vm.BatteryBadgeBackground);
            Assert.Equal("#26262B", vm.BatteryBadgeBorderBrush);
        });
    }

    [Fact]
    public void Dashboard_RefreshDashboardState_WhenPowerModeChangesInSettings_UpdatesRadioButtonsImmediately()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            int origMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            try
            {
                // Test switching to Quiet
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Quiet);
                window.RefreshDashboardState();
                Assert.True(window.BtnPowerQuiet.IsChecked == true);
                Assert.False(window.BtnPowerBal.IsChecked == true);
                Assert.False(window.BtnPowerPerf.IsChecked == true);

                // Test switching to Performance
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);
                window.RefreshDashboardState();
                Assert.False(window.BtnPowerQuiet.IsChecked == true);
                Assert.False(window.BtnPowerBal.IsChecked == true);
                Assert.True(window.BtnPowerPerf.IsChecked == true);

                // Test switching to Balanced
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Balanced);
                window.RefreshDashboardState();
                Assert.False(window.BtnPowerQuiet.IsChecked == true);
                Assert.True(window.BtnPowerBal.IsChecked == true);
                Assert.False(window.BtnPowerPerf.IsChecked == true);
            }
            finally
            {
                SettingsManager.Save("LastPowerMode", origMode);
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_PowerModeCard_HasExpectedStructureTooltipsAndStyling()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                Assert.NotNull(window.BtnPowerQuiet);
                Assert.NotNull(window.BtnPowerBal);
                Assert.NotNull(window.BtnPowerPerf);
                Assert.NotNull(window.BtnPowerTurbo);

                Assert.Equal("QUIET", window.BtnPowerQuiet.Content?.ToString());
                Assert.Equal("BALANCED", window.BtnPowerBal.Content?.ToString());
                Assert.Equal("PERFORMANCE", window.BtnPowerPerf.Content?.ToString());
                Assert.Equal("TURBO", window.BtnPowerTurbo.Content?.ToString());

                Assert.Equal("Power", window.BtnPowerQuiet.GroupName);
                Assert.Equal("Power", window.BtnPowerBal.GroupName);
                Assert.Equal("Power", window.BtnPowerPerf.GroupName);
                Assert.Equal("Power", window.BtnPowerTurbo.GroupName);

                Assert.Equal("Quiet", window.BtnPowerQuiet.CommandParameter?.ToString());
                Assert.Equal("Balanced", window.BtnPowerBal.CommandParameter?.ToString());
                Assert.Equal("Performance", window.BtnPowerPerf.CommandParameter?.ToString());
                Assert.Equal("Turbo", window.BtnPowerTurbo.CommandParameter?.ToString());

                Assert.False(string.IsNullOrWhiteSpace(window.BtnPowerQuiet.ToolTip?.ToString()));
                Assert.False(string.IsNullOrWhiteSpace(window.BtnPowerBal.ToolTip?.ToString()));
                Assert.False(string.IsNullOrWhiteSpace(window.BtnPowerPerf.ToolTip?.ToString()));
                Assert.False(string.IsNullOrWhiteSpace(window.BtnPowerTurbo.ToolTip?.ToString()));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_MinimizationAndRestoration_ControlsPollingLifecycle()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                var vm = (DashboardViewModel)window.DataContext;
                Assert.True(vm.IsPollingActive, "Polling should initially be active.");

                // Drive the state handler directly: real WindowState changes need a
                // shown HWND, which is environment-fragile in the test host.
                window.HandleWindowStateChanged(WindowState.Minimized);
                Assert.False(vm.IsPollingActive, "Polling should be paused on minimization.");

                window.HandleWindowStateChanged(WindowState.Normal);
                Assert.True(vm.IsPollingActive, "Polling should resume on restoration.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_InteractiveControls_TooltipPolicy_IsMinimalAndDescriptive()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                // Materialize templates without showing the window: measure/arrange
                // the content root directly. Shown HWNDs are environment-fragile in
                // the test host and previously left the tree unmaterialized.
                window.ApplyTemplate();
                var root = window.WindowRoot;
                root.Measure(new System.Windows.Size(375, 580));
                root.Arrange(new System.Windows.Rect(0, 0, 375, 580));
                root.UpdateLayout();

                for (int i = 0; i < 50; i++)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    System.Threading.Thread.Sleep(10);
                    if (window.GpuPage?.GpuCoreSlider != null && window.DashPage?.BtnPowerQuiet != null
                        && FindCloseButtonByTooltip(root) != null)
                        break;
                }

                // Updated tooltip policy: tooltips are a fallback for ambiguity, not decoration.
                // 1. Any tooltip that exists must be a meaningful, non-empty string.
                // 2. Interaction points whose behavior is not visible on screen must keep a tooltip
                //    (clamped ranges, AC/DC differences, tray side effects, hotkey capture).
                var violations = new List<string>();

                void CheckElement(DependencyObject depObj)
                {
                    int childrenCount = VisualTreeHelper.GetChildrenCount(depObj);
                    for (int i = 0; i < childrenCount; i++)
                    {
                        var child = VisualTreeHelper.GetChild(depObj, i);

                        if (child is FrameworkElement fe)
                        {
                            object? tooltip = fe.ToolTip ?? ToolTipService.GetToolTip(fe);
                            string name = string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.Name;

                            switch (tooltip)
                            {
                                case string s when string.IsNullOrWhiteSpace(s):
                                    violations.Add($"{fe.GetType().Name} (Name: '{name}') has an empty tooltip string.");
                                    break;
                                case System.Windows.Controls.ToolTip tt when tt.Content == null
                                    || string.IsNullOrWhiteSpace(tt.Content.ToString()):
                                    violations.Add($"{fe.GetType().Name} (Name: '{name}') has an empty ToolTip element.");
                                    break;
                            }
                        }

                        CheckElement(child);
                    }
                }

                CheckElement(root);

                Assert.True(
                    violations.Count == 0,
                    $"Found {violations.Count} tooltip policy violations:\n" + string.Join("\n", violations));

                // Non-obvious interaction points that must retain descriptive tooltips.
                Assert.NotNull(window.GpuPage);
                Assert.NotNull(window.DashPage);
                var gpuPage = window.GpuPage;
                var dashPage = window.DashPage;

                var mandatory = new (string Label, object? Tooltip)[]
                {
                    ("GPU core slider (clamped range)", gpuPage.GpuCoreSlider.ToolTip),
                    ("GPU memory slider (clamped range)", gpuPage.GpuMemorySlider.ToolTip),
                    ("Power QUIET (AC/DC caps)", dashPage.BtnPowerQuiet.ToolTip),
                    ("Power BALANCED (AC/DC caps)", dashPage.BtnPowerBal.ToolTip),
                    ("Power PERFORMANCE (AC/DC boost difference)", dashPage.BtnPowerPerf.ToolTip),
                    ("Power TURBO (AC-only full boost)", dashPage.BtnPowerTurbo.ToolTip),
                    ("Refresh AUTO (AC/DC rates)", dashPage.BtnRefreshAuto.ToolTip),
                    ("Fan MANUAL (curve behavior)", dashPage.BtnFanCustom.ToolTip),
                    ("Fan CURVE toggle", dashPage.BtnCurveToggle.ToolTip),
                };

                foreach (var (label, tooltip) in mandatory)
                {
                    Assert.False(string.IsNullOrWhiteSpace(tooltip?.ToString()), $"Missing mandatory tooltip: {label}");
                }

                var closeButton = FindVisualChildByName<Button>(root, "CloseWindowButton") ?? FindCloseButtonByTooltip(root);
                Assert.True(closeButton != null && !string.IsNullOrWhiteSpace(ToolTipService.GetToolTip(closeButton)?.ToString()),
                    "Close button must explain the tray side effect.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FansRail_SticksToBottom_WhenLcdOverdriveRowVisible()
    {
        RunInSta(() =>
        {
            // No window needed: explicit measure/arrange lays out the page deterministically.
            var page = new DashboardPageView();

            // Simulate an LCD-overdrive-capable panel: the Refresh card grows,
            // the Fans bottom rail must stay pinned to the Fans card bottom.
            var lcdRow = page.FindName("LcdOverdriveRow") as FrameworkElement;
            var rail = page.FindName("FansRail") as FrameworkElement;
            var card = page.FindName("FansCard") as FrameworkElement;
            Assert.NotNull(lcdRow);
            Assert.NotNull(rail);
            Assert.NotNull(card);
            lcdRow.Visibility = Visibility.Visible;

            page.Measure(new System.Windows.Size(375, 1000));
            page.Arrange(new System.Windows.Rect(0, 0, 375, 1000));
            page.UpdateLayout();

            Assert.True(rail.ActualHeight > 0 && card.ActualHeight > 0);
            var railBottom = rail.TranslatePoint(new System.Windows.Point(0, rail.ActualHeight), card);
            double gap = card.ActualHeight - railBottom.Y;
            // Card bottom padding (12) + content host margin (1) + tolerance.
            Assert.True(gap <= 16, $"Fans rail floats {gap:0}px above the card bottom; it must stay pinned.");
        });
    }

    [Fact]
    public void DashboardPage_WhenLcdOverdriveVisible_FansRailStaysInsideViewport()
    {
        RunInSta(() =>
        {
            var page = new DashboardPageView();
            var lcdRow = page.FindName("LcdOverdriveRow") as FrameworkElement;
            var rail = page.FindName("FansRail") as FrameworkElement;
            Assert.NotNull(lcdRow);
            Assert.NotNull(rail);
            lcdRow.Visibility = Visibility.Visible;

            // Dashboard window is 580 high: 40 title + 46 nav leaves 494 for the page.
            page.Measure(new System.Windows.Size(375, 494));
            page.Arrange(new System.Windows.Rect(0, 0, 375, 494));
            page.UpdateLayout();

            var railBottom = rail.TranslatePoint(new System.Windows.Point(0, rail.ActualHeight), page);
            Assert.True(railBottom.Y <= 495,
                $"Fans rail bottom at {railBottom.Y:0}px exceeds the 494px page viewport (clipped).");
        });
    }

    [Fact]
    public void PowerRail_CheckedButton_KeepsSurroundingDividers()
    {
        RunInSta(() =>
        {
            var page = new DashboardPageView();
            page.Measure(new System.Windows.Size(375, 1000));
            page.Arrange(new System.Windows.Rect(0, 0, 375, 1000));
            page.UpdateLayout();

            var bal = page.FindName("BtnPowerBal") as RadioButton;
            var perf = page.FindName("BtnPowerPerf") as RadioButton;
            Assert.NotNull(bal);
            Assert.NotNull(perf);

            bal.IsChecked = true;
            page.UpdateLayout();

            var ownDivider = FindVisualChildByName<System.Windows.Controls.Border>(bal, "LeftDivider");
            var nextDivider = FindVisualChildByName<System.Windows.Controls.Border>(perf, "LeftDivider");
            var mainBorder = FindVisualChildByName<System.Windows.Controls.Border>(bal, "MainBorder");
            Assert.NotNull(ownDivider);
            Assert.NotNull(nextDivider);
            Assert.NotNull(mainBorder);

            Assert.Equal(1.0, ownDivider.Opacity);
            Assert.Equal(1.0, nextDivider.Opacity);
            Assert.Equal(new Thickness(0), mainBorder.Margin);

            // The divider must render above the button surface: same-cell siblings
            // paint in child order, so a divider declared before MainBorder is
            // covered by the active segment's opaque fill.
            var templateRoot = VisualTreeHelper.GetParent(ownDivider) as System.Windows.Controls.Panel;
            Assert.NotNull(templateRoot);
            Assert.True(templateRoot.Children.IndexOf(ownDivider) > templateRoot.Children.IndexOf(mainBorder),
                "LeftDivider must be declared after MainBorder or the active fill covers it.");
        });
    }

    private static T? FindVisualChildByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && typed.Name == name)
            {
                return typed;
            }

            var result = FindVisualChildByName<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private static Button? FindCloseButtonByTooltip(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b && ToolTipService.GetToolTip(b) is string s && s.Contains("tray", StringComparison.OrdinalIgnoreCase))
            {
                return b;
            }

            var result = FindCloseButtonByTooltip(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    [Fact]
    public void DashboardViewModel_SyncSettings_UpdatesPropertiesFromRegistry()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            try
            {
                int originalPowerMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                string originalFanMode = SettingsManager.Get("LastFanMode", "Auto");
                int originalCpuSpd = SettingsManager.Get("CustomFanSpeedCpu", 50);

                try
                {
                    SettingsManager.Save("LastPowerMode", (int)PowerProfile.Quiet);
                    SettingsManager.Save("LastFanMode", "Medium");
                    SettingsManager.Save("CustomFanSpeedCpu", 75);

                    vm.SyncSettings();

                    Assert.Equal(PowerProfile.Quiet, vm.ActivePowerProfile);
                    Assert.True(vm.IsCustomFanEnabled);
                    Assert.Equal(75, vm.CpuFanSpeed);
                }
                finally
                {
                    SettingsManager.Save("LastPowerMode", originalPowerMode);
                    SettingsManager.Save("LastFanMode", originalFanMode);
                    SettingsManager.Save("CustomFanSpeedCpu", originalCpuSpd);
                }
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void DashboardPageView_EcoButton_HiddenOnAcAndShownOnBattery()
    {
        RunInSta(() =>
        {
            AcerWmiManager.SetPowerStateReaderForTesting(() => (true, 100));
            var vm = new DashboardViewModel();
            try
            {
                vm.IsEcoSupported = true;
                var page = new DashboardPageView { DataContext = vm };
                var eco = (FrameworkElement)page.FindName("BtnPowerEco");
                Assert.NotNull(eco);

                // Bindings activate on load, so assert the wiring itself: ECO visibility must
                // follow the same battery-and-support gate the view model drives.
                var binding = System.Windows.Data.BindingOperations.GetBinding(eco, UIElement.VisibilityProperty);
                Assert.NotNull(binding);
                Assert.Equal(nameof(DashboardViewModel.IsEcoModeAvailable), binding.Path.Path);
                Assert.IsType<System.Windows.Controls.BooleanToVisibilityConverter>(binding.Converter);

                var quietBinding = System.Windows.Data.BindingOperations
                    .GetBinding((FrameworkElement)page.FindName("BtnPowerQuiet"), UIElement.VisibilityProperty);
                Assert.Equal(nameof(DashboardViewModel.IsQuietVisible), quietBinding?.Path.Path);

                Assert.False(vm.IsEcoModeAvailable);

                AcerWmiManager.SetPowerStateReaderForTesting(() => (false, 80));
                vm.RefreshPowerModeAvailability();
                Assert.True(vm.IsEcoModeAvailable);
            }
            finally
            {
                AcerWmiManager.SetPowerStateReaderForTesting(null);
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_EcoVisibilityAndWhisperStatus_FollowBatteryAndMode()
    {
        RunInSta(() =>
        {
            int originalEco = SettingsManager.Get("IsEcoSupported", -1);
            AcerWmiManager.SetPowerStateReaderForTesting(() => (true, 100));
            var vm = new DashboardViewModel();
            try
            {
                vm.IsEcoSupported = true;

                // AC: ECO stays hidden even though the hardware supports it.
                Assert.False(vm.IsEcoModeAvailable);

                AcerWmiManager.SetPowerStateReaderForTesting(() => (false, 80));
                vm.RefreshPowerModeAvailability();
                Assert.True(vm.IsEcoModeAvailable);
                Assert.False(vm.IsQuietVisible);

                // Unsupported hardware keeps it hidden on battery too.
                vm.IsEcoSupported = false;
                Assert.False(vm.IsEcoModeAvailable);
                Assert.True(vm.IsQuietVisible);

                // Whisper chip follows the confirmed NVAPI state, not the requested mode.
                SettingsManager.Save(NvidiaGpuManager.WhisperStateKey, NvidiaGpuManager.WhisperStateOn);
                vm.RefreshWhisperState();
                Assert.True(vm.IsWhisperActive);
                Assert.Equal("WHISPER ON", vm.WhisperStatusText);

                SettingsManager.Save(NvidiaGpuManager.WhisperStateKey, NvidiaGpuManager.WhisperStateUnavailable);
                vm.RefreshWhisperState();
                Assert.False(vm.IsWhisperActive);
                Assert.Equal("WHISPER N/A", vm.WhisperStatusText);

                SettingsManager.Save(NvidiaGpuManager.WhisperStateKey, NvidiaGpuManager.WhisperStateOff);
                vm.RefreshWhisperState();
                Assert.Equal("WHISPER OFF", vm.WhisperStatusText);
            }
            finally
            {
                AcerWmiManager.SetPowerStateReaderForTesting(null);
                if (originalEco == -1) SettingsManager.Delete("IsEcoSupported");
                else SettingsManager.Save("IsEcoSupported", originalEco);
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_ActiveFanMode_DrivesRailAndSliderState()
    {
        RunInSta(() =>
        {
            int originalCpu = SettingsManager.Get("CustomFanSpeedCpu", 50);
            int originalGpu = SettingsManager.Get("CustomFanSpeedGpu", 50);
            var vm = new DashboardViewModel();
            try
            {
                vm.SyncActiveFanMode(FanProfile.Medium);
                Assert.True(vm.IsCustomFanMode);
                Assert.False(vm.IsAutoFanMode);
                Assert.True(vm.IsCustomFanEnabled);
                Assert.True(vm.IsManualSliderEnabled);

                vm.SyncActiveFanMode(FanProfile.Auto);
                Assert.True(vm.IsAutoFanMode);
                Assert.False(vm.IsCustomFanMode);
                Assert.False(vm.IsCustomFanEnabled);
                Assert.False(vm.IsManualSliderEnabled);

                // CPU and GPU sliders are independent manual channels.
                vm.CpuFanSpeed = 40;
                vm.GpuFanSpeed = 70;
                Assert.Equal(40, vm.CpuFanSpeed);
                Assert.Equal(70, vm.GpuFanSpeed);
            }
            finally
            {
                SettingsManager.Save("CustomFanSpeedCpu", originalCpu);
                SettingsManager.Save("CustomFanSpeedGpu", originalGpu);
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void Dashboard_RefreshDashboardState_SyncsFanRailFromSettings()
    {
        RunInSta(() =>
        {
            string originalFan = SettingsManager.GetString("LastFanMode", "Auto");
            var window = new NitrousDashboard();
            try
            {
                SettingsManager.Save("LastFanMode", FanProfile.Medium.ToString());
                window.RefreshDashboardState();
                Assert.True(window.BtnFanCustom.IsChecked);
                Assert.False(window.BtnFanAuto.IsChecked);

                SettingsManager.Save("LastFanMode", FanProfile.Auto.ToString());
                window.RefreshDashboardState();
                Assert.True(window.BtnFanAuto.IsChecked);
                Assert.False(window.BtnFanCustom.IsChecked);
            }
            finally
            {
                SettingsManager.Save("LastFanMode", originalFan);
                window.Close();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_PowerModeGate_ReflectsAcAndCharge()
    {
        RunInSta(() =>
        {
            int originalExpert = SettingsManager.Get("ExpertMode", 0);
            var vm = new DashboardViewModel();
            try
            {
                SettingsManager.Save("ExpertMode", 0);

                AcerWmiManager.SetPowerStateReaderForTesting(() => (false, 100));
                vm.RefreshPowerModeAvailability();
                Assert.False(vm.IsOverclockModeAvailable);
                Assert.Equal(Visibility.Visible, vm.PowerModeLockVisibility);
                Assert.Contains("ON BATTERY", vm.PowerModeLockText);
                Assert.Contains("on battery", vm.PerformanceTooltip, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("on battery", vm.TurboTooltip, StringComparison.OrdinalIgnoreCase);

                AcerWmiManager.SetPowerStateReaderForTesting(() => (true, 100));
                vm.RefreshPowerModeAvailability();
                Assert.True(vm.IsOverclockModeAvailable);
                Assert.Equal(Visibility.Collapsed, vm.PowerModeLockVisibility);
                Assert.Contains("More power", vm.PerformanceTooltip);
                Assert.Contains("Maximum power", vm.TurboTooltip);

                AcerWmiManager.SetPowerStateReaderForTesting(() => (true, 10));
                vm.RefreshPowerModeAvailability();
                Assert.False(vm.IsOverclockModeAvailable);
                Assert.Contains("BELOW", vm.PowerModeLockText);
                Assert.Contains("below 40%", vm.PerformanceTooltip, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("below 40%", vm.TurboTooltip, StringComparison.OrdinalIgnoreCase);

                SettingsManager.Save("ExpertMode", 1);
                vm.RefreshPowerModeAvailability();
                Assert.True(vm.IsOverclockModeAvailable);
            }
            finally
            {
                AcerWmiManager.SetPowerStateReaderForTesting(null);
                SettingsManager.Save("ExpertMode", originalExpert);
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_FanControlLock_FollowsModeAndExpertMode()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            try
            {
                int originalPowerMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                int originalExpert = SettingsManager.Get("ExpertMode", 0);
                try
                {
                    SettingsManager.Save("ExpertMode", 0);

                    vm.ActivePowerProfile = PowerProfile.Quiet;
                    Assert.True(vm.IsFanControlLocked);
                    Assert.False(vm.IsFanControlEnabled);
                    Assert.Equal(Visibility.Visible, vm.FanLockVisibility);
                    Assert.Contains("QUIET", vm.FanLockText);

                    vm.ActivePowerProfile = PowerProfile.Turbo;
                    Assert.True(vm.IsFanControlLocked);

                    vm.ActivePowerProfile = PowerProfile.Balanced;
                    Assert.False(vm.IsFanControlLocked);
                    Assert.Equal(Visibility.Collapsed, vm.FanLockVisibility);

                    SettingsManager.Save("ExpertMode", 1);
                    vm.ActivePowerProfile = PowerProfile.Quiet;
                    Assert.False(vm.IsFanControlLocked);
                }
                finally
                {
                    SettingsManager.Save("LastPowerMode", originalPowerMode);
                    SettingsManager.Save("ExpertMode", originalExpert);
                }
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_Properties_UsageAndMaxStatsExist()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            try
            {
                // Verify initial values of recently added UI properties
                Assert.NotNull(vm.CpuUsageText);
                Assert.NotNull(vm.GpuUsageText);
                Assert.NotNull(vm.CpuMaxStatsText);
                Assert.NotNull(vm.GpuMaxStatsText);

                // Modify to ensure they trigger INotifyPropertyChanged
                var triggeredProperties = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null)
                        triggeredProperties.Add(e.PropertyName);
                };

                vm.CpuUsageText = "10%";
                vm.GpuUsageText = "10%";
                vm.CpuMaxStatsText = "Max: 10";
                vm.GpuMaxStatsText = "Max: 10";

                Assert.Contains(nameof(vm.CpuUsageText), triggeredProperties);
                Assert.Contains(nameof(vm.GpuUsageText), triggeredProperties);
                Assert.Contains(nameof(vm.CpuMaxStatsText), triggeredProperties);
                Assert.Contains(nameof(vm.GpuMaxStatsText), triggeredProperties);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_UpdateGpuHover_WhenGpuIsAsleep_ShowsSleepIconAndHidesStats()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            try
            {
                var gpuHistoryField = typeof(DashboardViewModel).GetField("_gpuHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var historyList = (List<DashboardViewModel.TelemetryPoint>)gpuHistoryField!.GetValue(vm)!;
                
                long now = Environment.TickCount64;
                historyList.Add(new DashboardViewModel.TelemetryPoint { Timestamp = now, Temp = 0, Usage = 0 });

                vm.UpdateGpuHover(pixelX: 162.5, actualWidth: 162.5);

                Assert.Equal(Visibility.Visible, vm.GpuHoverVisibility);
                Assert.Equal(Visibility.Visible, vm.GpuHoverSleepVisibility);
                Assert.Equal(Visibility.Collapsed, vm.GpuHoverStatsVisibility);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public void DashboardViewModel_UpdateGpuHover_WhenGpuIsAwake_ShowsStatsAndHidesSleepIcon()
    {
        RunInSta(() =>
        {
            var vm = new DashboardViewModel();
            try
            {
                var gpuHistoryField = typeof(DashboardViewModel).GetField("_gpuHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var historyList = (List<DashboardViewModel.TelemetryPoint>)gpuHistoryField!.GetValue(vm)!;
                
                long now = Environment.TickCount64;
                historyList.Add(new DashboardViewModel.TelemetryPoint { Timestamp = now, Temp = 50, Usage = 30 });

                vm.UpdateGpuHover(pixelX: 162.5, actualWidth: 162.5);

                Assert.Equal(Visibility.Visible, vm.GpuHoverVisibility);
                Assert.Equal(Visibility.Collapsed, vm.GpuHoverSleepVisibility);
                Assert.Equal(Visibility.Visible, vm.GpuHoverStatsVisibility);
                Assert.Equal("50°C", vm.GpuHoverTempStr);
                Assert.Equal("30%", vm.GpuHoverUsageStr);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    // =========================================================================
    // 2. TrayApplication Windows Forms UI Tests
    // =========================================================================

    [Fact]
    public void TrayApplication_InitializesWithNotifyIconAndContextMenu()
    {
        RunInSta(() =>
        {
            using var tray = new TrayApplication();

            Assert.NotNull(tray.TrayIcon);
            Assert.True(tray.TrayIcon.Visible, "Tray icon must be visible.");
            Assert.NotNull(tray.ContextMenu);
            Assert.True(tray.ContextMenu.Items.Count >= 7, "Context menu must have at least 7 items.");
        });
    }

    [Fact]
    public void TrayApplication_ContextMenu_ContainsAllExpectedMenuItems()
    {
        RunInSta(() =>
        {
            using var tray = new TrayApplication();
            var menu = tray.ContextMenu;
            Assert.NotNull(menu);

            // 1. "Open Nitrous" (First item, bold font)
            var openItem = menu.Items[0] as ToolStripMenuItem;
            Assert.NotNull(openItem);
            Assert.Equal("Open Nitrous", openItem.Text);
            Assert.True(openItem.Font.Bold, "'Open Nitrous' menu item must use bold font.");

            // 2. Search for required items by text
            var itemTexts = menu.Items.OfType<ToolStripItem>()
                                      .Select(item => item.Text)
                                      .Where(t => !string.IsNullOrEmpty(t))
                                      .ToList();

            Assert.Contains("Open Nitrous", itemTexts);
            Assert.Contains("Power Profile", itemTexts);
            Assert.Contains("Fan Profile", itemTexts);
            Assert.Contains("Check for Updates...", itemTexts);
            Assert.Contains("Restart", itemTexts);
            Assert.Contains("Exit", itemTexts);
        });
    }

    [Fact]
    public void TrayApplication_PowerProfileSubmenu_HasValidItemsAndExclusiveCheck()
    {
        RunInSta(() =>
        {
            using var tray = new TrayApplication();
            var menu = tray.ContextMenu;
            Assert.NotNull(menu);

            var powerItem = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == "Power Profile");
            Assert.NotNull(powerItem);
            Assert.True(powerItem.DropDownItems.Count >= 3, "Power menu must contain at least Quiet, Balanced, Performance.");

            var dropDownTexts = powerItem.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
            Assert.Contains("Quiet", dropDownTexts);
            Assert.Contains("Balanced", dropDownTexts);
            Assert.Contains("Performance", dropDownTexts);

            // Exactly one power profile must be checked
            int checkedCount = powerItem.DropDownItems.OfType<ToolStripMenuItem>().Count(i => i.Checked);
            Assert.Equal(1, checkedCount);
        });
    }

    [Fact]
    public void TrayApplication_FanProfileSubmenu_HasValidItemsAndExclusiveCheck()
    {
        RunInSta(() =>
        {
            using var tray = new TrayApplication();
            var menu = tray.ContextMenu;
            Assert.NotNull(menu);

            var fanItem = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == "Fan Profile");
            Assert.NotNull(fanItem);
            Assert.Equal(3, fanItem.DropDownItems.Count);

            var dropDownTexts = fanItem.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
            Assert.Contains("Auto", dropDownTexts);
            Assert.Contains("Max", dropDownTexts);
            Assert.Contains("Custom", dropDownTexts);

            // Exactly one fan profile must be checked
            int checkedCount = fanItem.DropDownItems.OfType<ToolStripMenuItem>().Count(i => i.Checked);
            Assert.Equal(1, checkedCount);
        });
    }

    [Fact]
    public void TrayApplication_Disposal_HidesNotifyIconToPreventGhostIcons()
    {
        RunInSta(() =>
        {
            var tray = new TrayApplication();
            Assert.True(tray.TrayIcon.Visible);

            // Dispose should set Visible = false and clean up notify icon
            tray.Dispose();
            Assert.False(tray.TrayIcon.Visible, "Tray icon Visible must be false after disposal.");
        });
    }

    [Theory]
    [InlineData(true, false, PowerProfile.Performance, PowerProfile.Quiet, PowerProfile.Quiet)] // AC -> DC (Unplugged)
    [InlineData(false, true, PowerProfile.Quiet, PowerProfile.Performance, PowerProfile.Performance)] // DC -> AC (Plugged in)
    public void TrayApplication_AutoSwitch_WhenPowerSourceChangesAndProfileSwitches_TriggersOsd(
        bool initialAcState,
        bool newAcState,
        PowerProfile initialProfile,
        PowerProfile targetProfile,
        PowerProfile expectedOsdProfile)
    {
        RunInSta(() =>
        {
            int origAutoSwitch = SettingsManager.Get("AutoSwitch", 0);
            int origLastPower = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            int origAcPower = SettingsManager.Get("LastAcPowerMode", (int)PowerProfile.Performance);
            int origDcPower = SettingsManager.Get("LastDcPowerMode", (int)PowerProfile.Quiet);

            try
            {
                SettingsManager.Save("AutoSwitch", 1);
                SettingsManager.Save("LastPowerMode", (int)initialProfile);
                SettingsManager.Save("LastAcPowerMode", (int)(newAcState ? targetProfile : initialProfile));
                SettingsManager.Save("LastDcPowerMode", (int)(newAcState ? initialProfile : targetProfile));

                using var tray = new TrayApplication();
                tray.WasOnAcPower = initialAcState;
                tray.LastOsdProfile = null;

                tray.ApplyPowerSettings(isStartup: false, isOnlineOverride: newAcState);

                Assert.Equal(expectedOsdProfile, tray.LastOsdProfile);
                Assert.Equal((int)expectedOsdProfile, SettingsManager.Get("LastPowerMode", -1));
            }
            finally
            {
                SettingsManager.Save("AutoSwitch", origAutoSwitch);
                SettingsManager.Save("LastPowerMode", origLastPower);
                SettingsManager.Save("LastAcPowerMode", origAcPower);
                SettingsManager.Save("LastDcPowerMode", origDcPower);
            }
        });
    }

    [Fact]
    public void TrayApplication_AutoSwitch_WhenProfileDoesNotChange_SuppressesOsd()
    {
        RunInSta(() =>
        {
            int origAutoSwitch = SettingsManager.Get("AutoSwitch", 0);
            int origLastPower = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            int origAcPower = SettingsManager.Get("LastAcPowerMode", (int)PowerProfile.Performance);
            int origDcPower = SettingsManager.Get("LastDcPowerMode", (int)PowerProfile.Quiet);

            try
            {
                SettingsManager.Save("AutoSwitch", 1);
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Quiet);
                SettingsManager.Save("LastAcPowerMode", (int)PowerProfile.Quiet);
                SettingsManager.Save("LastDcPowerMode", (int)PowerProfile.Quiet);

                using var tray = new TrayApplication();
                tray.WasOnAcPower = true;
                tray.LastOsdProfile = null;

                // Unplugging, but target profile on DC is also Quiet (same as current)
                tray.ApplyPowerSettings(isStartup: false, isOnlineOverride: false);

                Assert.Null(tray.LastOsdProfile);
            }
            finally
            {
                SettingsManager.Save("AutoSwitch", origAutoSwitch);
                SettingsManager.Save("LastPowerMode", origLastPower);
                SettingsManager.Save("LastAcPowerMode", origAcPower);
                SettingsManager.Save("LastDcPowerMode", origDcPower);
            }
        });
    }

    [Fact]
    public void TrayApplication_AutoSwitch_WhenAutoSwitchDisabled_SuppressesOsd()
    {
        RunInSta(() =>
        {
            int origAutoSwitch = SettingsManager.Get("AutoSwitch", 0);
            int origLastPower = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            int origAcPower = SettingsManager.Get("LastAcPowerMode", (int)PowerProfile.Performance);
            int origDcPower = SettingsManager.Get("LastDcPowerMode", (int)PowerProfile.Quiet);

            try
            {
                SettingsManager.Save("AutoSwitch", 0);
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);
                SettingsManager.Save("LastAcPowerMode", (int)PowerProfile.Performance);
                SettingsManager.Save("LastDcPowerMode", (int)PowerProfile.Quiet);

                using var tray = new TrayApplication();
                tray.WasOnAcPower = true;
                tray.LastOsdProfile = null;

                // Unplugging with AutoSwitch disabled
                tray.ApplyPowerSettings(isStartup: false, isOnlineOverride: false);

                Assert.Null(tray.LastOsdProfile);
            }
            finally
            {
                SettingsManager.Save("AutoSwitch", origAutoSwitch);
                SettingsManager.Save("LastPowerMode", origLastPower);
                SettingsManager.Save("LastAcPowerMode", origAcPower);
                SettingsManager.Save("LastDcPowerMode", origDcPower);
            }
        });
    }

    [Fact]
    public void TrayApplication_AutoSwitch_OnStartup_SuppressesOsd()
    {
        RunInSta(() =>
        {
            int origAutoSwitch = SettingsManager.Get("AutoSwitch", 0);
            int origLastPower = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            int origAcPower = SettingsManager.Get("LastAcPowerMode", (int)PowerProfile.Performance);
            int origDcPower = SettingsManager.Get("LastDcPowerMode", (int)PowerProfile.Quiet);

            try
            {
                SettingsManager.Save("AutoSwitch", 1);
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);
                SettingsManager.Save("LastAcPowerMode", (int)PowerProfile.Performance);
                SettingsManager.Save("LastDcPowerMode", (int)PowerProfile.Quiet);

                using var tray = new TrayApplication();
                tray.WasOnAcPower = null;
                tray.LastOsdProfile = null;

                // Initial boot / startup apply
                tray.ApplyPowerSettings(isStartup: true, isOnlineOverride: false);

                Assert.Null(tray.LastOsdProfile);
            }
            finally
            {
                SettingsManager.Save("AutoSwitch", origAutoSwitch);
                SettingsManager.Save("LastPowerMode", origLastPower);
                SettingsManager.Save("LastAcPowerMode", origAcPower);
                SettingsManager.Save("LastDcPowerMode", origDcPower);
            }
        });
    }

    [Fact]
    public void DashboardViewModel_GpuAlert_ShowAndDismiss_UpdatesPropertiesAndVisibility()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();

            // Initial state: Alert must be collapsed
            Assert.Equal(Visibility.Collapsed, vm.GpuAlertVisibility);

            // Warning alert (e.g. Battery deferred or D3Cold sleep)
            vm.ShowGpuAlert(DashboardViewModel.GpuAlertLevel.Warning, "Battery Notice", "Tuning deferred on battery.");
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.Equal("Battery Notice", vm.GpuAlertTitle);
            Assert.Equal("Tuning deferred on battery.", vm.GpuAlertMessage);
            Assert.Equal("#E85D2A", vm.GpuAlertForeground);
            Assert.Equal("#08080A", vm.GpuAlertBackground);
            Assert.Equal("#E85D2A", vm.GpuAlertBorder);

            // Success alert
            vm.ShowGpuAlert(DashboardViewModel.GpuAlertLevel.Success, "Applied", "Clocks committed.");
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.Equal("#4CAF7D", vm.GpuAlertForeground);
            Assert.Equal("#08080A", vm.GpuAlertBackground);
            Assert.Equal("#4CAF7D", vm.GpuAlertBorder);

            // Error alert
            vm.ShowGpuAlert(DashboardViewModel.GpuAlertLevel.Error, "Rejected", "Driver rejected offsets.");
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.Equal("#E5484D", vm.GpuAlertForeground);
            Assert.Equal("#08080A", vm.GpuAlertBackground);
            Assert.Equal("#E5484D", vm.GpuAlertBorder);

            // Dismiss via method
            vm.DismissGpuAlert();
            Assert.Equal(Visibility.Collapsed, vm.GpuAlertVisibility);

            // Show again and dismiss via Command
            vm.ShowGpuAlert(DashboardViewModel.GpuAlertLevel.Info, "Info", "Some info");
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            vm.DismissGpuAlertCommand.Execute(null);
            Assert.Equal(Visibility.Collapsed, vm.GpuAlertVisibility);
        });
    }

    [Fact]
    public void DashboardViewModel_ApplyGpuClocks_WhenApplying_ShowsInformativeAlert()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();
            vm.GpuCoreOffset = 50;
            vm.GpuMemoryOffset = 100;

            // Trigger clock application
            vm.ApplyGpuClocksCommand.Execute(null);

            // An informative alert explaining the outcome must be displayed
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.NotEmpty(vm.GpuAlertTitle);
            Assert.NotEmpty(vm.GpuAlertMessage);
        });
    }

    [Fact]
    public void DashboardViewModel_ResetGpuClocks_ShowsResetConfirmationAlert()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();
            vm.GpuCoreOffset = 100;
            vm.GpuMemoryOffset = 200;

            // Trigger reset
            vm.ResetGpuClocksCommand.Execute(null);

            // Reset alert must be displayed
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.Equal("Clocks Reset to Stock", vm.GpuAlertTitle);
            Assert.Contains("0 MHz", vm.GpuAlertMessage);
        });
    }

    [Fact]
    public void DashboardViewModel_IsDebuggingEnabled_TogglesSettingsAndLogManager()
    {
        RunInSta(() =>
        {
            SettingsManager.Save("Debugging", false);
            SettingsManager.Save("DebugLogging", false);
            LogManager.IsDebugEnabled = false;

            using var vm = new DashboardViewModel();

            vm.IsDebuggingEnabled = true;
            Assert.True(LogManager.IsDebugEnabled);
            Assert.True(SettingsManager.Get("Debugging", false));

            vm.IsDebuggingEnabled = false;
            Assert.False(LogManager.IsDebugEnabled);
            Assert.False(SettingsManager.Get("Debugging", true));
        });
    }

    [Fact]
    public void DashboardViewModel_AutoApplyGpuClocks_TogglesAndSavesSetting()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();

            vm.AutoApplyGpuClocks = false;
            Assert.False(vm.AutoApplyGpuClocks);
            Assert.Equal(0, SettingsManager.Get("AutoApplyGpuClocks", 1));

            vm.AutoApplyGpuClocks = true;
            Assert.True(vm.AutoApplyGpuClocks);
            Assert.Equal(1, SettingsManager.Get("AutoApplyGpuClocks", 0));
        });
    }


    [Fact]
    public void DashboardViewModel_CopyDiagnosticsCommand_ShowsConfirmationAlert()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();

            vm.CopyDiagnosticsCommand.Execute(null);

            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.Equal("Diagnostics Copied", vm.GpuAlertTitle);
            Assert.Contains("diagnostic report", vm.GpuAlertMessage);
        });
    }

    [Fact]
    public void Dashboard_DiagnosticsUIElements_ArePresentAndHaveTooltips()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                Assert.NotNull(window.TogDebugging);
                Assert.NotNull(window.BtnOpenLogsFolder);
                Assert.NotNull(window.BtnCopyDiagnostics);
                Assert.NotNull(window.TogAutoApplyClocks);

                Assert.NotNull(window.TogDebugging.ToolTip);
                Assert.NotEmpty(window.TogDebugging.ToolTip.ToString() ?? "");

                Assert.NotNull(window.BtnOpenLogsFolder.ToolTip);
                Assert.NotEmpty(window.BtnOpenLogsFolder.ToolTip.ToString() ?? "");

                Assert.NotNull(window.BtnCopyDiagnostics.ToolTip);
                Assert.NotEmpty(window.BtnCopyDiagnostics.ToolTip.ToString() ?? "");

                Assert.NotNull(window.TogAutoApplyClocks.ToolTip);
                Assert.NotEmpty(window.TogAutoApplyClocks.ToolTip.ToString() ?? "");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TrayApplication_ReloadHotkeysIfChanged_WhenUnchanged_DoesNotReload()
    {
        RunInSta(() =>
        {
            using var tray = new TrayApplication();
            tray.ReloadHotkeys();

            // When settings are identical to cached, ReloadHotkeysIfChanged must return false
            bool reloaded = tray.ReloadHotkeysIfChanged();
            Assert.False(reloaded, "Hotkeys must not be re-registered when settings have not changed.");
        });
    }

    [Fact]
    public void TrayApplication_ReloadHotkeysIfChanged_WhenSettingChanged_TriggersReload()
    {
        RunInSta(() =>
        {
            string origCycle = SettingsManager.Get("Hotkey_CyclePower", "");
            try
            {
                using var tray = new TrayApplication();
                tray.ReloadHotkeys();

                // Change hotkey setting
                SettingsManager.Save("Hotkey_CyclePower", "2|81");

                bool reloaded = tray.ReloadHotkeysIfChanged();
                Assert.True(reloaded, "Hotkeys must be reloaded when a hotkey setting changes.");

                // Second call with same setting should now return false
                bool reloadedAgain = tray.ReloadHotkeysIfChanged();
                Assert.False(reloadedAgain, "Hotkeys must not reload again once synchronized.");
            }
            finally
            {
                if (string.IsNullOrEmpty(origCycle))
                    SettingsManager.Delete("Hotkey_CyclePower");
                else
                    SettingsManager.Save("Hotkey_CyclePower", origCycle);
            }
        });
    }

    [Fact]
    public void AcerWmiManager_IsTurboModeSupported_CachesResultInStaticField()
    {
        try
        {
            AcerWmiManager.SetTurboSupportedForTesting(true);
            Assert.True(AcerWmiManager.IsTurboModeSupported());

            AcerWmiManager.SetTurboSupportedForTesting(false);
            Assert.False(AcerWmiManager.IsTurboModeSupported());
        }
        finally
        {
            AcerWmiManager.SetTurboSupportedForTesting(null);
        }
    }

    [Fact]
    public void DashboardViewModel_IsTurboSupported_WhenCachedZero_InitializesAsFalseWithoutFlicker()
    {
        RunInSta(() =>
        {
            int origTurbo = SettingsManager.Get("IsTurboSupported", -1);
            int origMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            try
            {
                SettingsManager.Save("IsTurboSupported", 0);
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Turbo);

                using var vm = new DashboardViewModel();

                // Must be false immediately on creation, and ActivePowerProfile must fallback from Turbo to Performance
                Assert.False(vm.IsTurboSupported, "IsTurboSupported must be false immediately when cached as unsupported.");
                Assert.Equal(PowerProfile.Performance, vm.ActivePowerProfile);
            }
            finally
            {
                if (origTurbo == -1)
                    SettingsManager.Delete("IsTurboSupported");
                else
                    SettingsManager.Save("IsTurboSupported", origTurbo);

                SettingsManager.Save("LastPowerMode", origMode);
            }
        });
    }

    [Fact]
    public void DashboardViewModel_IsTurboSupported_WhenCachedOne_InitializesAsTrueImmediately()
    {
        RunInSta(() =>
        {
            int origTurbo = SettingsManager.Get("IsTurboSupported", -1);
            try
            {
                SettingsManager.Save("IsTurboSupported", 1);

                using var vm = new DashboardViewModel();

                Assert.True(vm.IsTurboSupported, "IsTurboSupported must be true immediately when cached as supported.");
            }
            finally
            {
                if (origTurbo == -1)
                    SettingsManager.Delete("IsTurboSupported");
                else
                    SettingsManager.Save("IsTurboSupported", origTurbo);
            }
        });
    }

    [Fact]
    public void DashboardViewModel_SyncSettings_WhenTurboUnsupported_FallsBackToPerformance()
    {
        RunInSta(() =>
        {
            int origTurbo = SettingsManager.Get("IsTurboSupported", -1);
            int origMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            try
            {
                SettingsManager.Save("IsTurboSupported", 0);
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);

                using var vm = new DashboardViewModel();
                Assert.Equal(PowerProfile.Performance, vm.ActivePowerProfile);

                // Simulate external change setting mode to Turbo
                SettingsManager.Save("LastPowerMode", (int)PowerProfile.Turbo);
                vm.SyncSettings();

                Assert.Equal(PowerProfile.Performance, vm.ActivePowerProfile);
            }
            finally
            {
                if (origTurbo == -1)
                    SettingsManager.Delete("IsTurboSupported");
                else
                    SettingsManager.Save("IsTurboSupported", origTurbo);

                SettingsManager.Save("LastPowerMode", origMode);
            }
        });
    }

    [Fact]
    public void DashboardViewModel_ResumePolling_WhenGpuAsleep_InitializesSleepStateImmediately()
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();
            // Simulate that the GPU was previously awake
            vm.GpuSleepOverlayVisibility = Visibility.Collapsed;
            vm.GpuUsageText = "45%";
            vm.GpuGraphOpacity = 1.0;

            // In testhost, IsTestHost() causes IsGpuAwake check in ResumePolling to resolve safely as false
            vm.ResumePolling();

            Assert.Equal(Visibility.Visible, vm.GpuSleepOverlayVisibility);
            Assert.Equal("Sleep", vm.GpuUsageText);
            Assert.Equal(0.3, vm.GpuGraphOpacity);
            Assert.Equal("NVIDIA GPU (SLEEPING)", vm.GpuNameText);
        });
    }

    [Fact]
    public void Dashboard_ChamferedShell_InitializesWithChamferClipAndPaletteTokens()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                // Verify chamfer clip is assigned and is a valid PathGeometry
                Assert.NotNull(window.WindowRoot.Clip);
                var rootClip = Assert.IsType<PathGeometry>(window.WindowRoot.Clip);
                Assert.Single(rootClip.Figures);
                Assert.True(rootClip.Figures[0].IsClosed);

                // Verify shell frame has chamfer geometry and Steel600 stroke
                Assert.NotNull(window.WindowShellFrame.Data);
                Assert.IsType<PathGeometry>(window.WindowShellFrame.Data);

                // Verify palette tokens are resolvable
                string[] igniteTokens =
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
                    "BrandAccentBrush",
                    "SurfaceDeepBrush",
                    "BorderShellBrush"
                };

                foreach (var token in igniteTokens)
                {
                    var res = window.TryFindResource(token);
                    Assert.True(res != null, $"Expected ignite token '{token}' to be defined in DashboardStyles.");
                }

                // Verify resizing updates the chamfer geometry
                window.Width = 400;
                window.Height = 700;
                window.UpdateChamferGeometry();

                var updatedClip = Assert.IsType<PathGeometry>(window.WindowRoot.Clip);
                var seg0 = Assert.IsType<LineSegment>(updatedClip.Figures[0].Segments[0]);
                Assert.Equal(390.0, seg0.Point.X);
                var seg1 = Assert.IsType<LineSegment>(updatedClip.Figures[0].Segments[1]);
                Assert.Equal(400.0, seg1.Point.X);
            }
            finally
            {
                window.Close();
            }
        });
    }
    [Fact]
    public void Dashboard_RefreshAndFanButtons_MouseHoverTransitions_DoNotThrowUnsetValueException()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();

                var page = window.DashPage;
                Assert.NotNull(page);

                // Enable manual custom fan to make curve buttons visible
                var vm = Assert.IsType<DashboardViewModel>(window.DataContext);
                vm.IsCustomFanEnabled = true;

                // Exercise mouse enter and leave on vertical detent refresh and fan buttons
                UIElement[] buttonsToHover =
                {
                    page.BtnRefreshAuto,
                    page.BtnRefresh60,
                    page.BtnRefreshMax,
                    page.BtnFanAuto,
                    page.BtnFanMax,
                    page.BtnFanCustom,
                    page.BtnCurveToggle,
                    page.BtnEditCurve
                };

                foreach (var element in buttonsToHover)
                {
                    Assert.NotNull(element);
                    element.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    {
                        RoutedEvent = System.Windows.UIElement.MouseEnterEvent
                    });
                    element.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    {
                        RoutedEvent = System.Windows.UIElement.MouseLeaveEvent
                    });
                }
            }
            finally
            {
                window.Close();
            }
        });
    }
    [Fact]
    public void Dashboard_FanWriteView_MeasuresAndArrangesCleanly()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();
                var vm = Assert.IsType<DashboardViewModel>(window.DataContext);
                vm.IsCurveModeEnabled = false;
                vm.IsCustomFanEnabled = true;
                Assert.True(vm.IsManualSliderEnabled);

                window.DashPage.Measure(new System.Windows.Size(375, 580));
                window.DashPage.Arrange(new System.Windows.Rect(0, 0, 375, 580));
                window.DashPage.UpdateLayout();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_PageMetrics_CompactMarginsAndBiggerComponents_RendersCleanly()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();
                Assert.Equal(580, window.Height);
                Assert.Equal(375, window.Width);

                // Verify BorderCardBrush is resolvable
                var borderCardBrush = window.TryFindResource("BorderCardBrush") as SolidColorBrush;
                Assert.NotNull(borderCardBrush);
                Assert.Equal(System.Windows.Media.Color.FromRgb(0x38, 0x38, 0x42), borderCardBrush.Color);

                // Measure and arrange at new compact height
                window.DashPage.Measure(new System.Windows.Size(375, 580));
                window.DashPage.Arrange(new System.Windows.Rect(0, 0, 375, 580));
                window.DashPage.UpdateLayout();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ToolTip_Style_IsChamferedAndWrapsLongText()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                var style = window.TryFindResource(typeof(System.Windows.Controls.ToolTip)) as Style;
                Assert.NotNull(style);

                // Width cap so long text folds instead of clipping off-screen.
                var maxWidthSetter = style.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.MaxWidthProperty);
                Assert.NotNull(maxWidthSetter);
                Assert.Equal(320.0, maxWidthSetter.Value);

                // Render a tooltip with long content: it must wrap and produce the chamfered Path frame.
                var tt = new System.Windows.Controls.ToolTip
                {
                    Content = "Cap charging at 80% to extend battery lifespan. " +
                              "This is deliberately long so the text has to wrap across multiple lines inside the shell.",
                    PlacementTarget = window.DashPage.BtnFanCustom,
                    Style = style
                };
                tt.IsOpen = true;
                tt.Measure(new System.Windows.Size(320, 400));
                tt.Arrange(new System.Windows.Rect(0, 0, 320, 400));
                tt.UpdateLayout();

                var path = FindVisualChildByName<System.Windows.Shapes.Path>(tt, "Frame");
                Assert.NotNull(path);
                var geometry = Assert.IsType<System.Windows.Media.PathGeometry>(path.Data);
                Assert.True(geometry.Figures.Count > 0 && geometry.Figures[0].Segments.Count == 5,
                    "Chamfered tooltip frame must be the 6-point chamfer polygon (5 line segments).");

                var textBlock = FindVisualChildByName<TextBlock>(tt, "");
                if (textBlock != null && textBlock.Text.Contains("deliberately long", StringComparison.Ordinal))
                {
                    Assert.Equal(TextWrapping.Wrap, textBlock.TextWrapping);
                }

                tt.IsOpen = false;
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ToolTip_WithDashboardContentPresenterTemplate_RendersWithoutUnsetValueException()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();
                var button = window.DashPage.BtnFanCustom;
                
                var tt = new System.Windows.Controls.ToolTip
                {
                    Content = "Enables custom fan speed controls and waypoint temperature curves.",
                    PlacementTarget = button
                };
                var style = window.DashPage.FindResource(typeof(System.Windows.Controls.ToolTip)) as Style;
                Assert.NotNull(style);
                tt.Style = style;
                tt.IsOpen = true;
                tt.Measure(new System.Windows.Size(280, 100));
                tt.Arrange(new System.Windows.Rect(0, 0, 280, 100));
                tt.UpdateLayout();
                tt.IsOpen = false;
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SmallGhostToggleBtn_Triggers_DoNotHaveUnresolvedStaticResourceUnsetValue()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                var style = (Style)window.FindResource("SmallGhostToggleBtn");
                Assert.NotNull(style);

                var templateSetter = style.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == System.Windows.Controls.Control.TemplateProperty);
                Assert.NotNull(templateSetter);
                var template = Assert.IsType<ControlTemplate>(templateSetter.Value);

                foreach (var trigger in template.Triggers.OfType<Trigger>())
                {
                    foreach (var setter in trigger.Setters.OfType<Setter>())
                    {
                        Assert.False(setter.Value == DependencyProperty.UnsetValue,
                            $"Trigger on {trigger.Property?.Name}={trigger.Value} has UnsetValue for property {setter.Property?.Name}");
                    }
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DashboardStyles_AllStylesAndTriggers_HaveNoUnresolvedStaticResources()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                var dict = window.Resources.MergedDictionaries.FirstOrDefault();
                Assert.NotNull(dict);

                foreach (var key in dict.Keys)
                {
                    if (dict[key] is Style style)
                    {
                        foreach (var setter in style.Setters.OfType<Setter>())
                        {
                            Assert.False(setter.Value == DependencyProperty.UnsetValue,
                                $"Style '{key}' setter for property {setter.Property?.Name} evaluated to UnsetValue.");
                        }

                        var templateSetter = style.Setters.OfType<Setter>()
                            .FirstOrDefault(s => s.Property == System.Windows.Controls.Control.TemplateProperty);
                        if (templateSetter?.Value is ControlTemplate template)
                        {
                            foreach (var trigger in template.Triggers.OfType<Trigger>())
                            {
                                foreach (var setter in trigger.Setters.OfType<Setter>())
                                {
                                    Assert.False(setter.Value == DependencyProperty.UnsetValue,
                                        $"Style '{key}' template trigger ({trigger.Property?.Name}={trigger.Value}) setter for property {setter.Property?.Name} evaluated to UnsetValue.");
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                window.Close();
            }
        });
    }
}
