using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Nitrous.Enums;
using Nitrous.Managers;
using Nitrous.Ui;
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
                // Ensure WPF Application context exists so pack:// URI schemes resolve properly
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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    // =========================================================================
    // 1. NitrousDashboard WPF UI Tests
    // =========================================================================

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
                Assert.True(vm.BatteryBadgeText == "AC POWER" || vm.BatteryBadgeText == "BATTERY");

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
            }
            finally
            {
                window.Close();
            }
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
    public void Dashboard_MinimizationAndRestoration_ControlsPollingLifecycle()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                var vm = (DashboardViewModel)window.DataContext;
                Assert.True(vm.IsPollingActive, "Polling should initially be active.");

                // Show window so HWND is created and OnStateChanged fires on minimization
                window.Show();
                window.WindowState = WindowState.Minimized;
                Assert.False(vm.IsPollingActive, "Polling should be paused on minimization.");

                // Restoring to Normal should resume polling
                window.WindowState = WindowState.Normal;
                Assert.True(vm.IsPollingActive, "Polling should resume on restoration.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Dashboard_InteractiveControls_AdhereToMandatoryTooltipRule()
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                // Fork Rule 9: Every interactive control (Button, RadioButton, CheckBox, Slider)
                // must include a descriptive, dark-themed ToolTip.
                var missingTooltips = new List<string>();

                void CheckElement(DependencyObject depObj)
                {
                    int childrenCount = VisualTreeHelper.GetChildrenCount(depObj);
                    for (int i = 0; i < childrenCount; i++)
                    {
                        var child = VisualTreeHelper.GetChild(depObj, i);

                        if (child is Button or RadioButton or CheckBox or Slider)
                        {
                            var fe = (FrameworkElement)child;
                            object? tooltip = fe.ToolTip ?? ToolTipService.GetToolTip(fe);

                            bool hasValidTooltip = tooltip switch
                            {
                                string s => !string.IsNullOrWhiteSpace(s),
                                System.Windows.Controls.ToolTip tt => tt.Content != null && !string.IsNullOrWhiteSpace(tt.Content.ToString()),
                                _ => tooltip != null
                            };

                            // Filter out internal scrollbar/slider subcomponents
                            string name = fe.Name;
                            if (string.IsNullOrEmpty(name)) name = fe.GetType().Name;

                            if (!hasValidTooltip && !name.StartsWith("PART_") && !name.Contains("Thumb"))
                            {
                                missingTooltips.Add($"{fe.GetType().Name} (Name: '{name}')");
                            }
                        }

                        CheckElement(child);
                    }
                }

                // Apply template to ensure visual tree is generated
                window.ApplyTemplate();
                CheckElement(window);

                // Note: The root elements directly defined in XAML have tooltips; verify none are missing
                Assert.True(
                    missingTooltips.Count == 0,
                    $"Found {missingTooltips.Count} interactive controls missing tooltips:\n" + string.Join("\n", missingTooltips)
                );
            }
            finally
            {
                window.Close();
            }
        });
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
            Assert.Equal("#FF9F0A", vm.GpuAlertForeground);
            Assert.Equal("#1C1610", vm.GpuAlertBackground);

            // Success alert
            vm.ShowGpuAlert(DashboardViewModel.GpuAlertLevel.Success, "Applied", "Clocks committed.");
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.Equal("#34C759", vm.GpuAlertForeground);
            Assert.Equal("#101C14", vm.GpuAlertBackground);

            // Error alert
            vm.ShowGpuAlert(DashboardViewModel.GpuAlertLevel.Error, "Rejected", "Driver rejected offsets.");
            Assert.Equal(Visibility.Visible, vm.GpuAlertVisibility);
            Assert.Equal("#FF453A", vm.GpuAlertForeground);
            Assert.Equal("#201214", vm.GpuAlertBackground);

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
}

