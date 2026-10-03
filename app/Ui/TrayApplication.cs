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

    private readonly GlobalHotkeyManager _hotkeys = new();
    private readonly OsdForm _osd = new();

    private const int HK_CYCLE_POWER = 1;
    private const int HK_DASHBOARD = 99;

    private static readonly Font BoldMenuFont = new(Control.DefaultFont, FontStyle.Bold);

    public static TrayApplication? Instance { get; private set; }

    public TrayApplication()
    {
        Instance = this;
        Icon appIcon = SystemIcons.Shield;
        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield; } catch { }

        trayIcon = new NotifyIcon { Icon = appIcon, Visible = true, Text = "Nitrous" };
        trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };

        BuildContextMenu();

        SystemEvents.PowerModeChanged += OnPowerStateChanged;

        _ = Task.Run(() => UpdateManager.CheckForUpdatesAsync(true, () => Exit(null, EventArgs.Empty)));

        _nitroHook = new NitroKeyHook();
        _nitroHook.NitroKeyPressed += (s, e) => ShowDashboard();

        _hotkeys.HotkeyPressed += OnCustomHotkeyPressed;
        ReloadHotkeys();

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

                    await _gpuManager.ApplyPowerProfileOcAsync(profile);
                    TriggerProfileOsd(profile);

                    foreach (ToolStripItem sibling in powerMenu.DropDownItems)
                    {
                        if (sibling is ToolStripMenuItem mi)
                            mi.Checked = (mi.Tag is PowerProfile p && p == profile);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set power mode: {ex.Message}");
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
                            string pName = activeMode.ToString();
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
                    Debug.WriteLine($"Failed to set fan mode: {ex.Message}");
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
                Debug.WriteLine($"Failed to check for updates: {ex.Message}");
            }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restart", null, Restart);
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

        var currentProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

        // Apply CPU Power Management
        if (SettingsManager.Get("ManageCpuPower", 0) == 1)
        {
            _ = CpuPowerManager.ApplyProfileLimitsAsync(currentProfile, isOnline);
        }
        else
        {
            _ = CpuPowerManager.RestoreDefaultsAsync(isOnline);
        }

        var refreshMode = (RefreshProfile)SettingsManager.Get("RefreshMode", (int)RefreshProfile.Auto);
        DisplayManager.ApplyRefreshProfile(refreshMode, isOnline);

        _ = _gpuManager.ApplyPowerProfileOcAsync(currentProfile);
    }

    private PowerProfile? _cachedProfile;
    private System.Collections.Generic.List<System.Windows.Point>? _cachedCpuCurve;
    private System.Collections.Generic.List<System.Windows.Point>? _cachedGpuCurve;
    private long _cachedCurveVersion = -1;
    private string _lastObservedFanMode = "";
    private bool _lastObservedCurveEnabled = false;
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
                    long currentCurveVersion = SettingsManager.Get("FanCurveVersion", 0L);

                    // Reset tracking state if fan mode or curve override changed
                    if (currentFanMode != _lastObservedFanMode || isCurveEnabled != _lastObservedCurveEnabled)
                    {
                        _lastObservedFanMode = currentFanMode;
                        _lastObservedCurveEnabled = isCurveEnabled;
                        _lastAppliedCpuSpeed = -1;
                        _lastAppliedGpuSpeed = -1;
                        _cpuDownstepHoldTicks = 0;
                        _gpuDownstepHoldTicks = 0;
                    }

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

                        if (isCurveActive && telemetry.CpuTemp > 0)
                        {
                            // 3. Load curves from cache or Registry if profile changed or curve updated
                            var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                            if (_cachedCpuCurve == null || _cachedGpuCurve == null || _cachedProfile != activeMode || _cachedCurveVersion != currentCurveVersion)
                            {
                                _cachedProfile = activeMode;
                                _cachedCurveVersion = currentCurveVersion;
                                string pName = activeMode.ToString();
                                _cachedCpuCurve = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{pName}", FanCurveHelper.GetDefaultCpuCurve(activeMode));
                                _cachedGpuCurve = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{pName}", FanCurveHelper.GetDefaultGpuCurve(activeMode));
                                _lastAppliedCpuSpeed = -1;
                                _lastAppliedGpuSpeed = -1;
                            }

                            // 4. Interpolate raw target speeds
                            int targetCpuSpeed = Math.Clamp(FanCurveHelper.InterpolateSpeed(_cachedCpuCurve, telemetry.CpuTemp), 0, 100);
                            int targetGpuSpeed = effectiveGpuTemp == 0
                                ? targetCpuSpeed
                                : Math.Clamp(FanCurveHelper.InterpolateSpeed(_cachedGpuCurve, effectiveGpuTemp), 0, 100);

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

    private void ReloadHotkeys()
    {
        _hotkeys.UnregisterAll(HK_CYCLE_POWER, HK_DASHBOARD);

        RegisterSavedHotkey("Hotkey_CyclePower", HK_CYCLE_POWER);
        RegisterSavedHotkey("Hotkey_Dashboard", HK_DASHBOARD);
    }

    private void RegisterSavedHotkey(string settingKey, int id)
    {
        string saved = SettingsManager.Get(settingKey, "");
        if (string.IsNullOrEmpty(saved)) return;

        try
        {
            // Example saved format: "2|81" (Modifiers=2 (Ctrl), Key=81 (Q))
            var parts = saved.Split('|');
            uint mods = uint.Parse(parts[0]);
            uint key = uint.Parse(parts[1]);
            _hotkeys.Register(id, mods, key);
        }
        catch { }
    }

    private void OnCustomHotkeyPressed(int id)
    {
        switch (id)
        {
            case HK_CYCLE_POWER:
                CyclePowerMode();
                break;
            case HK_DASHBOARD:
                ShowDashboard();
                break;
        }
    }

    private void CyclePowerMode()
    {
        bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        string acDcKey = isOnline ? "LastAcPowerMode" : "LastDcPowerMode";

        // Get the current mode
        var currentProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        PowerProfile nextProfile;

        // Determine the next mode in the sequence
        switch (currentProfile)
        {
            case PowerProfile.Quiet:
                nextProfile = PowerProfile.Balanced;
                break;
            case PowerProfile.Balanced:
                nextProfile = PowerProfile.Performance;
                break;
            case PowerProfile.Performance:
                // Skip Turbo and wrap around to Quiet if it isn't supported
                nextProfile = AcerWmiManager.IsTurboModeSupported() ? PowerProfile.Turbo : PowerProfile.Quiet;
                break;
            case PowerProfile.Turbo:
            default:
                nextProfile = PowerProfile.Quiet;
                break;
        }

        // Apply the new mode
        _ = AcerWmiManager.SetPowerModeAsync(nextProfile);
        SettingsManager.Save("LastPowerMode", (int)nextProfile);
        SettingsManager.Save(acDcKey, (int)nextProfile);

        if (SettingsManager.Get("ManageCpuPower", 0) == 1)
        {
            _ = CpuPowerManager.ApplyProfileLimitsAsync(nextProfile, isOnline);
        }

        _ = _gpuManager.ApplyPowerProfileOcAsync(nextProfile);
        TriggerProfileOsd(nextProfile);
    }

    public void TriggerProfileOsd(PowerProfile profile)
    {
        Color osdColor = profile switch
        {
            PowerProfile.Quiet => Color.FromArgb(52, 199, 89),       // Green
            PowerProfile.Balanced => Color.FromArgb(10, 132, 255),   // Blue
            PowerProfile.Performance => Color.FromArgb(255, 159, 10),// Orange
            PowerProfile.Turbo => Color.FromArgb(255, 69, 58),       // Red
            _ => Color.White
        };

        _osd.ShowProfile($"{profile} MODE", osdColor, profile);
    }

    private void CleanupResources()
    {
        // 1. Cancel background loops and unhook events
        _engineCts.Cancel();
        _nitroHook.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerStateChanged;

        // 2. Hide and dispose tray icon to prevent ghost icons
        trayIcon.Visible = false;
        trayIcon.ContextMenuStrip?.Dispose();
        trayIcon.Dispose();
        _gpuManager.Dispose();
        _engineCts.Dispose();
        _hotkeys.Dispose();

        // 3. Kill any open Dashboard UI processes BEFORE spawning a new one
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
    }

    private void Restart(object? sender, EventArgs e)
    {
        CleanupResources();
        Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
        Application.Exit();
    }

    private void Exit(object? sender, EventArgs e)
    {
        CleanupResources();
        Application.Exit();
    }
}
