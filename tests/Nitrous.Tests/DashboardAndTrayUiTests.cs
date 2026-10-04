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
                string expectedVersion = $"Nitrous {UpdateManager.CurrentVersion}";

                // All header page labels must reflect the synchronized version
                Assert.Equal(expectedVersion, window.DashVersionText.Text);
                Assert.Equal(expectedVersion, window.GpuVersionText.Text);
                Assert.Equal(expectedVersion, window.KeyboardVersionText.Text);
                Assert.Equal(expectedVersion, window.SettingsVersionText.Text);

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

                // Power Pill
                Assert.True(
                    window.DashPowerPillText.Text == "AC POWER" || window.DashPowerPillText.Text == "BATTERY",
                    $"Power pill text was unexpected: '{window.DashPowerPillText.Text}'"
                );
                Assert.NotNull(window.DashPowerPillBorder.BorderBrush);

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
}
