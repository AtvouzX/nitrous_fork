using System;
using System.Drawing;
using System.Windows.Forms;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;

namespace Nitrous.Ui;

public partial class TrayApplication : ApplicationContext
{
    private void BuildContextMenu()
    {
        trayIcon.ContextMenuStrip?.Dispose();
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, ShowItemToolTips = true };

        var openItem = new ToolStripMenuItem("Open Nitrous", null, (s, e) => ShowDashboard())
        {
            Font = BoldMenuFont
        };
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());

        // Power Profiles Submenu
        var powerMenu = new ToolStripMenuItem("Power Profile");

        void AddPowerItem(string name, PowerProfile profile)
        {
            var item = new ToolStripMenuItem(name, null, async (s, e) =>
            {
                try
                {
                    // Firmware refuses these modes on battery; resolve so the checkmark
                    // and the OSD show the mode that actually landed.
                    var resolved = AcerWmiManager.ResolvePowerProfileRequest(profile);
                    if (resolved != profile)
                    {
                        LogManager.Warn(
                            $"{profile} downgraded to {resolved}: AC power or charge level not met", "Tray");
                    }

                    if (ScenarioStore.IsEnabled && !ScenarioStore.IsSuspended)
                    {
                        ScenarioStore.SetSuspended(true);
                        LogManager.Info("Scenario suspended by manual power change in Tray menu", "Scenario");
                    }
                    SettingsManager.Save("LastPowerMode", (int)resolved);
                    await AcerWmiManager.ApplyPowerProfileAsync(resolved);
                    bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                    SettingsManager.Save(isOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)resolved);

                    if (SettingsManager.Get("ManageCpuPower", 0) == 1)
                    {
                        _ = CpuPowerManager.ApplyProfileLimitsAsync(resolved, isOnline);
                    }

                    await _gpuManager.ApplyProfileOcAsync(resolved, isOnline);
                    TriggerProfileOsd(resolved);

                    foreach (ToolStripItem sibling in powerMenu.DropDownItems)
                    {
                        if (sibling is ToolStripMenuItem mi)
                            mi.Checked = (mi.Tag is PowerProfile p && p == resolved);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Warn($"Failed to set power mode: {ex.Message}", "Tray");
                }
            })
            {
                Tag = profile
            };
            powerMenu.DropDownItems.Add(item);
        }

        // Performance and Turbo are refused by the firmware on battery (and below the
        // charge floor): grey them out with a reason instead of letting clicks no-op.
        powerMenu.DropDownOpening += (_, _) =>
        {
            bool overclockOk = AcerWmiManager.IsOverclockModeAvailable();
            var (onAc, _) = AcerWmiManager.GetPowerState();
            foreach (ToolStripItem entry in powerMenu.DropDownItems)
            {
                if (entry is not ToolStripMenuItem mi || mi.Tag is not PowerProfile p) continue;
                if (HardwareSafetyPolicy.RequiresAcGate(p))
                {
                    mi.Enabled = overclockOk;
                    mi.ToolTipText = overclockOk
                        ? string.Empty
                        : $"Needs AC power. On AC, {HardwareSafetyPolicy.MinimumBatteryPercentForOverclock}% charge unless Expert Mode is on.";
                }
                else if (HardwareSafetyPolicy.RequiresBatteryGate(p))
                {
                    mi.Enabled = !onAc;
                    mi.ToolTipText = !onAc
                        ? string.Empty
                        : "Eco mode is only available on battery.";
                }
            }
        };

        if (AcerWmiManager.IsEcoModeSupported())
        {
            AddPowerItem("Eco", PowerProfile.Eco);
        }
        AddPowerItem("Quiet", PowerProfile.Quiet);
        AddPowerItem("Balanced", PowerProfile.Balanced);
        AddPowerItem("Performance", PowerProfile.Performance);
        if (AcerWmiManager.IsTurboModeSupported())
        {
            AddPowerItem("Turbo", PowerProfile.Turbo);
        }

        menu.Items.Add(powerMenu);

        // Scenarios Submenu (rebuilt on open; Tray is sole applier)
        var scenarioMenu = new ToolStripMenuItem("Scenario");
        _scenarioMenu = scenarioMenu;
        menu.Opening += (_, _) => RebuildScenarioMenu();
        menu.Items.Add(scenarioMenu);

        // Fan Profiles Submenu
        var fanMenu = new ToolStripMenuItem("Fan Profile");

        // The active power mode owns the fan table in Quiet and Turbo: show that in the
        // menu itself instead of letting clicks silently do nothing.
        fanMenu.DropDownOpening += (_, _) =>
        {
            bool allowed = AcerWmiManager.IsFanControlAllowed();
            fanMenu.Text = allowed ? "Fan Profile" : "Fan Profile (locked)";
            fanMenu.ToolTipText = allowed
                ? string.Empty
                : "The active power mode runs the firmware fan table. Enable Expert Mode in Settings to force manual speeds.";
            foreach (ToolStripItem item in fanMenu.DropDownItems)
            {
                item.Enabled = allowed;
            }
        };

        void AddFanItem(string name, FanProfile profile)
        {
            var item = new ToolStripMenuItem(name, null, async (s, e) =>
            {
                try
                {
                    if (ScenarioStore.IsEnabled && !ScenarioStore.IsSuspended)
                    {
                        ScenarioStore.SetSuspended(true);
                        LogManager.Info("Scenario suspended by manual fan change in Tray menu", "Scenario");
                    }
                    _lastAppliedCpuSpeed = -1;
                    _lastAppliedGpuSpeed = -1;

                    if (!AcerWmiManager.IsFanControlAllowed())
                    {
                        LogManager.Warn("Fan control is disabled by the active power mode", "Tray");
                        return;
                    }

                    if (profile == FanProfile.Medium)
                    {
                        bool isCurveEnabled = SettingsManager.Get("IsCurveModeEnabled", 0) == 1;
                        if (isCurveEnabled)
                        {
                            var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                            string pName = SettingsManager.Get("ActiveFanCurvePreset", "Auto");
                            if (pName == "Auto") pName = activeMode.ToString();
                            
                            var cpuCurve = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{pName}", FanCurveHelper.GetDefaultCpuCurve(activeMode));
                            var gpuCurve = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{pName}", FanCurveHelper.GetDefaultGpuCurve(activeMode));
                            var telem = AcerWmiManager.GetSystemTelemetry();
                            int cpuSpd = FanCurveHelper.GetClampedSpeed(cpuCurve, telem.CpuTemp > 0 ? telem.CpuTemp : 50);
                            int gpuTemp = telem.GpuTemp > 0 ? telem.GpuTemp : telem.CpuTemp;
                            int gpuSpd = FanCurveHelper.GetClampedSpeed(gpuCurve, gpuTemp > 0 ? gpuTemp : 50);

                            _lastAppliedCpuSpeed = cpuSpd;
                            _lastAppliedGpuSpeed = gpuSpd;
                            await AcerWmiManager.SetFansAsync(FanProfile.Medium, cpuSpd, gpuSpd);
                        }
                        else
                        {
                            await AcerWmiManager.SetFansAsync(FanProfile.Medium, 
                                SettingsManager.Get("CustomFanSpeedCpu", 50),
                                SettingsManager.Get("CustomFanSpeedGpu", 50));
                        }
                    }
                    else
                    {
                        await AcerWmiManager.SetFansAsync(profile);
                    }
                    AcerWmiManager.SaveTrackedFanMode(
                        profile, SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);

                    foreach (ToolStripItem sibling in fanMenu.DropDownItems)
                    {
                        if (sibling is ToolStripMenuItem mi)
                            mi.Checked = (mi.Tag is FanProfile fp && fp == profile);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Warn($"Failed to set fan mode: {ex.Message}", "Tray");
                }
            })
            {
                Tag = profile
            };
            fanMenu.DropDownItems.Add(item);
        }

        AddFanItem("Auto", FanProfile.Auto);
        AddFanItem("Max", FanProfile.Max);
        AddFanItem("Custom", FanProfile.Medium);
        menu.Items.Add(fanMenu);

        void SyncMenuCheckedStates()
        {
            var currentPower = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            foreach (ToolStripItem item in powerMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem mi && mi.Tag is PowerProfile p)
                {
                    mi.Checked = (p == currentPower);
                }
            }

            string fanModeStr = SettingsManager.Get("LastFanMode", "Auto");
            var currentFan = Enum.TryParse(fanModeStr, out FanProfile f) ? f : FanProfile.Auto;
            foreach (ToolStripItem item in fanMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem mi && mi.Tag is FanProfile fp)
                {
                    mi.Checked = (fp == currentFan);
                }
            }
        }

        // Synchronize checkmarks immediately and whenever the menu or submenus open
        SyncMenuCheckedStates();
        menu.Opening += (s, e) => SyncMenuCheckedStates();
        powerMenu.DropDownOpening += (s, e) => SyncMenuCheckedStates();
        fanMenu.DropDownOpening += (s, e) => SyncMenuCheckedStates();

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Check for Updates...", null, async (s, e) =>
        {
            try
            {
                await UpdateManager.CheckForUpdatesAsync(false, () => Exit(null, EventArgs.Empty));
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to check for updates: {ex.Message}", "Tray");
            }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restart", null, Restart);
        menu.Items.Add("Exit", null, Exit);

        trayIcon.ContextMenuStrip = menu;
    }
}
