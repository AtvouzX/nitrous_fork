using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using Microsoft.Win32;
using Nitrous.Enums;
using Nitrous.Hooks;
using Nitrous.Managers;
using Nitrous.Helpers;

namespace Nitrous.Ui;

public class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly NitroKeyHook _nitroHook;
    private readonly NvidiaGpuManager _gpuManager = new();
    private bool? _wasOnAcPower = null;
    private int _powerEventId = 0;

    private CancellationTokenSource _engineCts = new CancellationTokenSource();
    private int _lastAppliedCpuSpeed = -1;
    private int _lastAppliedGpuSpeed = -1;

    private static readonly Font BoldMenuFont = new(Control.DefaultFont, FontStyle.Bold);

    public TrayApplication()
    {
        Icon appIcon = SystemIcons.Shield;
        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield; } catch { }

        trayIcon = new NotifyIcon { Icon = appIcon, Visible = true, Text = "Nitrous" };
        trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };

        BuildContextMenu();

        SystemEvents.PowerModeChanged += OnPowerStateChanged;

        _ = Task.Run(() => UpdateManager.CheckForUpdatesAsync(true, () => Exit(null, EventArgs.Empty)));

        _nitroHook = new NitroKeyHook();
        _nitroHook.NitroKeyPressed += (s, e) => ShowDashboard();

        _ = Task.Run(async () =>
        {
            // Initial quick memory trim after JIT compilation
            await Task.Delay(2000);
            MemoryHelper.TrimWorkingSet();

            await Task.Delay(6000);
            ApplyPowerSettings(true);

            var bootProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            await _gpuManager.ApplyOnBootAsync(bootProfile);

            // Final trim after all boot settings are applied
            MemoryHelper.TrimWorkingSet();
        });

        StartBackgroundEngine();
    }

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
                await AcerWmiManager.SetPowerModeAsync(profile);
                SettingsManager.Save("LastPowerMode", (int)profile);
                bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                SettingsManager.Save(isOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)profile);
                await _gpuManager.ApplyPowerProfileOcAsync(profile);

                foreach (ToolStripItem sibling in powerMenu.DropDownItems)
                {
                    if (sibling is ToolStripMenuItem mi)
                        mi.Checked = (mi.Tag is PowerProfile p && p == profile);
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
                if (profile == FanProfile.Medium)
                {
                    await AcerWmiManager.SetCustomFansAsync(
                        SettingsManager.Get("CustomFanSpeedCpu", 50),
                        SettingsManager.Get("CustomFanSpeedGpu", 50));
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
        menu.Items.Add("Check for Updates...", null, async (s, e) => await UpdateManager.CheckForUpdatesAsync(false, () => Exit(null, EventArgs.Empty)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, Exit);

        trayIcon.ContextMenuStrip = menu;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private void ShowDashboard()
    {
        using var currentProcess = Process.GetCurrentProcess();
        string processName = currentProcess.ProcessName;
        int currentId = currentProcess.Id;

        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var p in processes)
            {
                if (p.Id != currentId)
                {
                    // Found the existing UI process. Restore and bring to front.
                    IntPtr hWnd = p.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        const int SW_RESTORE = 9;
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                    return; // Prevent spawning a new instance
                }
            }
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }

        // If no UI process is running, start a new one
        var uiProc = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--ui") { UseShellExecute = true });
        if (uiProc != null)
        {
            try
            {
                uiProc.EnableRaisingEvents = true;
                uiProc.Exited += (s, e) =>
                {
                    try { uiProc.Dispose(); } catch { }
                    MemoryHelper.TrimWorkingSet();
                };
            }
            catch { }
        }
    }

    private async void OnPowerStateChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
        {
            int eventId = ++_powerEventId;
            await Task.Delay(3500);
            if (eventId != _powerEventId) return;

            ApplyPowerSettings(false);
            MemoryHelper.TrimWorkingSet();
        }
    }

    private void ApplyPowerSettings(bool isStartup = false)
    {
        bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;

        if (!isStartup && _wasOnAcPower.HasValue && _wasOnAcPower.Value == isOnline) return;
        _wasOnAcPower = isOnline;

        if (SettingsManager.Get("AutoSwitch", 0) == 1)
        {
            string keyMode = isOnline ? "LastAcPowerMode" : "LastDcPowerMode";
            var activeMode = (PowerProfile)SettingsManager.Get(keyMode, (int)(isOnline ? PowerProfile.Performance : PowerProfile.Quiet));
            _ = AcerWmiManager.SetPowerModeAsync(activeMode);
            SettingsManager.Save("LastPowerMode", (int)activeMode);

            string keyFan = isOnline ? "LastAcFanMode" : "LastDcFanMode";
            var activeFan = Enum.TryParse(SettingsManager.Get(keyFan, "Auto"), out FanProfile f) ? f : FanProfile.Auto;

            if (activeFan == FanProfile.Medium)
                _ = AcerWmiManager.SetCustomFansAsync(SettingsManager.Get("CustomFanSpeedCpu", 50), SettingsManager.Get("CustomFanSpeedGpu", 50));
            else
                _ = AcerWmiManager.SetFansAsync(activeFan);

            SettingsManager.Save("LastFanMode", activeFan.ToString());
        }

        var refreshMode = (RefreshProfile)SettingsManager.Get("RefreshMode", (int)RefreshProfile.Auto);
        DisplayManager.ApplyRefreshProfile(refreshMode, isOnline);

        var currentProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        _ = _gpuManager.ApplyPowerProfileOcAsync(currentProfile);
    }

    private PowerProfile? _cachedProfile;
    private System.Collections.Generic.List<System.Windows.Point>? _cachedCpuCurve;
    private System.Collections.Generic.List<System.Windows.Point>? _cachedGpuCurve;
    private int _cpuDownstepHoldTicks;
    private int _gpuDownstepHoldTicks;
    private const int HysteresisHoldCycles = 2; // 2 cycles * 2s = 4s delay before stepping down

    private void StartBackgroundEngine()
    {
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            int tickCounter = 0;

            while (!_engineCts.Token.IsCancellationRequested)
            {
                try
                {
                    tickCounter++;

                    // 1. Check if the user wants Custom Fan Mode and if the Curve is enabled
                    string currentFanMode = SettingsManager.Get("LastFanMode", "Auto");
                    bool isCurveEnabled = SettingsManager.Get("IsCurveModeEnabled", 0) == 1;
                    bool isCurveActive = currentFanMode == "Medium" && isCurveEnabled;

                    // If curve is active: poll telemetry and evaluate curves every 2s (1 tick).
                    // If curve is NOT active: hardware EC handles fan speeds, so poll every 10s (every 5 ticks) for tooltip.
                    if (isCurveActive || (tickCounter % 5 == 0))
                    {
                        var telemetry = AcerWmiManager.GetSystemTelemetry();
                        int effectiveGpuTemp = telemetry.GpuTemp;

                        string gpuTip = effectiveGpuTemp > 0 ? $"{effectiveGpuTemp}°C" : "Sleep";
                        string tipText = $"Nitrous | CPU: {telemetry.CpuTemp}°C  GPU: {gpuTip}";
                        string truncatedTip = tipText.Length > 63 ? tipText[..63] : tipText;
                        if (trayIcon.Text != truncatedTip)
                        {
                            trayIcon.Text = truncatedTip;
                        }

                        if (isCurveActive)
                        {
                            // 3. Load curves from cache or Registry if profile changed
                            var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                            if (_cachedCpuCurve == null || _cachedGpuCurve == null || _cachedProfile != activeMode)
                            {
                                _cachedProfile = activeMode;
                                string pName = activeMode.ToString();
                                _cachedCpuCurve = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{pName}", FanCurveHelper.GetDefaultCpuCurve(activeMode));
                                _cachedGpuCurve = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{pName}", FanCurveHelper.GetDefaultGpuCurve(activeMode));
                            }

                            // 4. Interpolate raw target speeds
                            int targetCpuSpeed = FanCurveHelper.InterpolateSpeed(_cachedCpuCurve, telemetry.CpuTemp);
                            int targetGpuSpeed = effectiveGpuTemp == 0
                                ? targetCpuSpeed
                                : FanCurveHelper.InterpolateSpeed(_cachedGpuCurve, effectiveGpuTemp);

                            // 5. Apply Hysteresis (Anti-Revving):
                            // Ramp-up: Immediate (protects hardware)
                            // Ramp-down: Hold for HysteresisHoldCycles (4s) before stepping down
                            int appliedCpuSpeed = _lastAppliedCpuSpeed;
                            if (_lastAppliedCpuSpeed == -1 || targetCpuSpeed >= _lastAppliedCpuSpeed)
                            {
                                appliedCpuSpeed = targetCpuSpeed;
                                _cpuDownstepHoldTicks = 0;
                            }
                            else
                            {
                                _cpuDownstepHoldTicks++;
                                if (_cpuDownstepHoldTicks >= HysteresisHoldCycles)
                                {
                                    appliedCpuSpeed = targetCpuSpeed;
                                    _cpuDownstepHoldTicks = 0;
                                }
                            }

                            int appliedGpuSpeed = _lastAppliedGpuSpeed;
                            if (_lastAppliedGpuSpeed == -1 || targetGpuSpeed >= _lastAppliedGpuSpeed)
                            {
                                appliedGpuSpeed = targetGpuSpeed;
                                _gpuDownstepHoldTicks = 0;
                            }
                            else
                            {
                                _gpuDownstepHoldTicks++;
                                if (_gpuDownstepHoldTicks >= HysteresisHoldCycles)
                                {
                                    appliedGpuSpeed = targetGpuSpeed;
                                    _gpuDownstepHoldTicks = 0;
                                }
                            }

                            // 6. Fire WMI only if changed
                            if (appliedCpuSpeed != _lastAppliedCpuSpeed || appliedGpuSpeed != _lastAppliedGpuSpeed)
                            {
                                _lastAppliedCpuSpeed = appliedCpuSpeed;
                                _lastAppliedGpuSpeed = appliedGpuSpeed;
                                await AcerWmiManager.SetCustomFansAsync(appliedCpuSpeed, appliedGpuSpeed);
                            }
                        }
                    }

                    // Periodic memory purge every ~60 seconds (every 30 ticks)
                    if (tickCounter % 30 == 0)
                    {
                        GC.Collect(1, GCCollectionMode.Default, false);
                        MemoryHelper.TrimWorkingSet();
                    }
                }
                catch { /* Absorb exceptions to keep background engine alive */ }

                await timer.WaitForNextTickAsync(_engineCts.Token);
            }
        });
    }

    private void Exit(object? sender, EventArgs e)
    {
        _engineCts.Cancel();
        _nitroHook.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerStateChanged;
        trayIcon.Visible = false;
        trayIcon.ContextMenuStrip?.Dispose();
        trayIcon.Dispose();
        _gpuManager.Dispose();
        _engineCts.Dispose();

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            string pName = currentProcess.ProcessName;
            int currentId = currentProcess.Id;
            int currentSessionId = currentProcess.SessionId;

            var processes = Process.GetProcessesByName(pName);
            foreach (var p in processes)
            {
                using (p)
                {
                    if (p.Id != currentId && p.SessionId == currentSessionId)
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
        }
        catch { }

        Application.Exit();
    }
}
