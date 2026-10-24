using System.Diagnostics;
using Microsoft.Win32;
using Nitrous.Enums;
using Nitrous.Hooks;
using Nitrous.Managers;
using Nitrous.Helpers;

namespace Nitrous.Ui;

public partial class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly NitroKeyHook? _nitroHook;
    private readonly NvidiaGpuManager _gpuManager = new();

    internal ContextMenuStrip? ContextMenu => trayIcon?.ContextMenuStrip;
    internal NotifyIcon TrayIcon => trayIcon;
    private bool? _wasOnAcPower = null;
    internal bool? WasOnAcPower { get => _wasOnAcPower; set => _wasOnAcPower = value; }
    private bool _wasGpuAwake;
    internal bool WasGpuAwake { get => _wasGpuAwake; set => _wasGpuAwake = value; }
    internal PowerProfile? LastOsdProfile { get; set; }
    private int _powerEventId = 0;

    private CancellationTokenSource _engineCts = new CancellationTokenSource();
    private int _lastAppliedCpuSpeed = -1;
    private int _lastAppliedGpuSpeed = -1;
    private RegisteredWaitHandle? _settingsSyncReg;
    private EventWaitHandle? _settingsSyncEvent;

    private readonly GlobalHotkeyManager _hotkeys = new();
    private readonly OsdForm _osd = new();
    private string _lastCyclePowerHotkey = string.Empty;
    private string _lastDashboardHotkey = string.Empty;

    private const int HK_CYCLE_POWER = 1;
    private const int HK_DASHBOARD = 99;

    private static readonly Font BoldMenuFont = new(Control.DefaultFont, FontStyle.Bold);

    public static TrayApplication? Instance { get; private set; }

    private static int _exitResetExecuted = 0;

    internal static bool TriggerExitFanReset()
    {
        if (SettingsManager.Get("ResetFansOnExit", 1) != 1) return false;
        if (Interlocked.Exchange(ref _exitResetExecuted, 1) == 1) return false;

        return AcerWmiManager.ResetFansToAutoSync();
    }

    internal static void ResetExitFlagForTesting()
    {
        Interlocked.Exchange(ref _exitResetExecuted, 0);
    }

    public TrayApplication()
    {
        Instance = this;
        Icon appIcon = SystemIcons.Shield;
        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield; }
        catch (Exception ex) { LogManager.Debug($"Could not extract app icon: {ex.Message}", "Tray"); }

        trayIcon = new NotifyIcon { Icon = appIcon, Visible = true, Text = "Nitrous" };
        trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };

        BuildContextMenu();

        // Ensure _osd handle exists on the UI thread for cross-thread marshalling
        _ = _osd.Handle;

        if (!IsTestHost())
        {
            LogManager.Info("TrayApplication initializing background engine...", "Tray");
            SystemEvents.PowerModeChanged += OnPowerStateChanged;
            SystemEvents.SessionEnding += OnSessionEnding;

            try
            {
                _settingsSyncEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.SettingsChangedEventName);
                _settingsSyncReg = ThreadPool.RegisterWaitForSingleObject(_settingsSyncEvent, (state, timedOut) =>
                {
                    LogManager.SyncSettings();
                    ReloadHotkeysIfChanged();
                }, null, Timeout.Infinite, false);
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to register settings sync listener in Tray: {ex.Message}", "Tray");
            }

            _ = Task.Run(() => UpdateManager.CheckForUpdatesAsync(true, () => Exit(null, EventArgs.Empty)));

            _nitroHook = new NitroKeyHook();
            _nitroHook.NitroKeyPressed += (s, e) => ShowDashboard();

            _hotkeys.HotkeyPressed += OnCustomHotkeyPressed;
            ReloadHotkeys();

            _ = Task.Run(async () =>
            {
                // Clean up any legacy IFEO registry entries, dummy System32 files, or AppData scripts
                NitroKeyManager.PurgeLegacyIfeoIntegration();

                // Initial quick memory trim after JIT compilation
                await Task.Delay(2000);
                MemoryHelper.TrimWorkingSet();

                await Task.Delay(6000);
                ApplyPowerSettings(true);

                bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                if (isOnline)
                {
                    var bootProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                    await _gpuManager.ApplyOnBootAsync(bootProfile, isOnline);
                }

                // Final trim after all boot settings are applied
                MemoryHelper.TrimWorkingSet();
            });

            StartBackgroundEngine();
        }
    }



    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int ASFW_ANY = -1;

    private void ShowDashboard()
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
        }
        catch (Exception ex)
        {
            LogManager.Debug($"AllowSetForegroundWindow failed: {ex.Message}", "Tray");
        }

        // 1. Fast signal: If a dashboard process is already running and listening, signal it to restore/activate
        try
        {
            if (EventWaitHandle.TryOpenExisting(Program.DashboardEventName, out var showEvent))
            {
                using (showEvent)
                {
                    showEvent.Set();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"DashboardEvent signal check failed: {ex.Message}", "Tray");
        }

        // 2. Mutex check: If a dashboard is in the middle of launching, avoid spawning a duplicate
        try
        {
            if (Mutex.TryOpenExisting(Program.DashboardMutexName, out var uiMutex))
            {
                using (uiMutex)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Dashboard mutex check failed: {ex.Message}", "Tray");
        }

        // 3. Fallback Win32 check by window title
        IntPtr existingHwnd = FindWindow(null, "Nitrous Dashboard");
        if (existingHwnd != IntPtr.Zero)
        {
            const int SW_RESTORE = 9;
            ShowWindow(existingHwnd, SW_RESTORE);
            SetForegroundWindow(existingHwnd);
            return;
        }

        // 4. If no UI process is running, start a new one
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
            catch (Exception ex)
            {
                LogManager.Debug($"Failed configuring dashboard process exit handler: {ex.Message}", "Tray");
            }
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

            // Defer aggressive working set trimming until after OSD display & fade animations finish
            _ = Task.Delay(4000).ContinueWith(_ => MemoryHelper.TrimWorkingSet(), TaskScheduler.Default);
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        LogManager.Info($"Session ending (Reason: {e.Reason}). Triggering fan reset to Auto.", "Tray");
        TriggerExitFanReset();
    }

    internal void ApplyPowerSettings(bool isStartup = false, bool? isOnlineOverride = null)
    {
        bool isOnline = isOnlineOverride ?? (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);

        if (!isStartup && _wasOnAcPower.HasValue && _wasOnAcPower.Value == isOnline) return;
        _wasOnAcPower = isOnline;

        if (SettingsManager.Get("AutoSwitch", 0) == 1)
        {
            var previousMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
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

            if (!isStartup && activeMode != previousMode)
            {
                TriggerProfileOsd(activeMode);
            }
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

        _ = _gpuManager.ApplyPowerProfileOcAsync(currentProfile, isOnline);
    }



    internal void ReloadHotkeys()
    {
        _lastCyclePowerHotkey = SettingsManager.Get("Hotkey_CyclePower", "");
        _lastDashboardHotkey = SettingsManager.Get("Hotkey_Dashboard", "");

        _hotkeys.UnregisterAll(HK_CYCLE_POWER, HK_DASHBOARD);

        RegisterSavedHotkey("Hotkey_CyclePower", HK_CYCLE_POWER);
        RegisterSavedHotkey("Hotkey_Dashboard", HK_DASHBOARD);
    }

    internal bool ReloadHotkeysIfChanged()
    {
        string cyclePower = SettingsManager.Get("Hotkey_CyclePower", "");
        string dashboard = SettingsManager.Get("Hotkey_Dashboard", "");

        if (cyclePower == _lastCyclePowerHotkey && dashboard == _lastDashboardHotkey)
        {
            return false;
        }

        if (!_osd.IsDisposed && _osd.IsHandleCreated && _osd.InvokeRequired)
        {
            try
            {
                _osd.BeginInvoke(new Action(ReloadHotkeys));
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Debug($"Failed to marshal ReloadHotkeys to UI thread: {ex.Message}", "Tray");
            }
        }

        ReloadHotkeys();
        return true;
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
        catch (Exception ex)
        {
            LogManager.Debug($"Failed registering hotkey ID {id} ('{saved}'): {ex.Message}", "Tray");
        }
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

        _ = _gpuManager.ApplyPowerProfileOcAsync(nextProfile, isOnline);
        TriggerProfileOsd(nextProfile);
    }

    public void TriggerProfileOsd(PowerProfile profile)
    {
        LastOsdProfile = profile;
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


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupResources();
        }
        base.Dispose(disposing);
    }

    private void CleanupResources()
    {
        // 0. Synchronously reset fan speeds to Auto before disposing resources
        TriggerExitFanReset();

        // 1. Cancel background loops and unhook events
        _settingsSyncReg?.Unregister(null);
        _settingsSyncEvent?.Dispose();
        _engineCts.Cancel();
        _nitroHook?.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerStateChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;

        // 2. Hide and dispose tray icon to prevent ghost icons
        trayIcon.Visible = false;
        trayIcon.ContextMenuStrip?.Dispose();
        trayIcon.Dispose();
        _osd.Dispose();
        _gpuManager.Dispose();
        _engineCts.Dispose();
        _hotkeys.Dispose();

        if (!IsTestHost())
        {
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
    }

    private static bool IsTestHost()
    {
        string procName = Process.GetCurrentProcess().ProcessName;
        return procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
               procName.Contains("vstest", StringComparison.OrdinalIgnoreCase);
    }

    private void Restart(object? sender, EventArgs e)
    {
        CleanupResources();
        Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
        Application.Exit();
    }

    private void Exit(object? sender, EventArgs e)
    {
        LogManager.Info("TrayApplication exit requested", "Tray");
        CleanupResources();
        Application.Exit();
    }
}
