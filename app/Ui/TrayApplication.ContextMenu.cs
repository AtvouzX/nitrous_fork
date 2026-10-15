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
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };

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
                    await AcerWmiManager.SetPowerModeAsync(profile);
                    SettingsManager.Save("LastPowerMode", (int)profile);
                    bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                    SettingsManager.Save(isOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)profile);

                    if (SettingsManager.Get("ManageCpuPower", 0) == 1)
                    {
                        _ = CpuPowerManager.ApplyProfileLimitsAsync(profile, isOnline);
                    }

                    await _gpuManager.ApplyPowerProfileOcAsync(profile, isOnline);
                    TriggerProfileOsd(profile);

                    foreach (ToolStripItem sibling in powerMenu.DropDownItems)
                    {
                        if (sibling is ToolStripMenuItem mi)
                            mi.Checked = (mi.Tag is PowerProfile p && p == profile);
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

        AddPowerItem("Quiet", PowerProfile.Quiet);
        AddPowerItem("Balanced", PowerProfile.Balanced);
        AddPowerItem("Performance", PowerProfile.Performance);
        if (AcerWmiManager.IsTurboModeSupported())
        {
            AddPowerItem("Turbo", PowerProfile.Turbo);
        }

        menu.Items.Add(powerMenu);

        // Fan Profiles Submenu
        var fanMenu = new ToolStripMenuItem("Fan Profile");

        void AddFanItem(string name, FanProfile profile)
        {
            var item = new ToolStripMenuItem(name, null, async (s, e) =>
            {
                try
                {
                    _lastAppliedCpuSpeed = -1;
                    _lastAppliedGpuSpeed = -1;

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
                            int cpuSpd = Math.Clamp(FanCurveHelper.InterpolateSpeed(cpuCurve, telem.CpuTemp > 0 ? telem.CpuTemp : 50), 0, 100);
                            int gpuTemp = telem.GpuTemp > 0 ? telem.GpuTemp : telem.CpuTemp;
                            int gpuSpd = Math.Clamp(FanCurveHelper.InterpolateSpeed(gpuCurve, gpuTemp > 0 ? gpuTemp : 50), 0, 100);

                            _lastAppliedCpuSpeed = cpuSpd;
                            _lastAppliedGpuSpeed = gpuSpd;
                            await AcerWmiManager.SetCustomFansAsync(cpuSpd, gpuSpd);
                        }
                        else
                        {
                            await AcerWmiManager.SetCustomFansAsync(
                                SettingsManager.Get("CustomFanSpeedCpu", 50),
                                SettingsManager.Get("CustomFanSpeedGpu", 50));
                        }
                    }
                    else
                    {
                        await AcerWmiManager.SetFansAsync(profile);
                    }
                    SettingsManager.Save("LastFanMode", profile.ToString());
                    bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                    SettingsManager.Save(isOnline ? "LastAcFanMode" : "LastDcFanMode", profile.ToString());

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
