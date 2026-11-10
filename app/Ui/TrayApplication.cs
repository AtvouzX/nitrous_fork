using System.Diagnostics;
using Microsoft.Win32;
using Nitrous.Enums;
using Nitrous.Hooks;
using Nitrous.Managers;
using Nitrous.Helpers;
using Nitrous.Interop;

namespace Nitrous.Ui;

public partial class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly NitroKeyHook? _nitroHook;
    private readonly NvidiaGpuManager _gpuManager = new();

    internal ContextMenuStrip? ContextMenu => trayIcon?.ContextMenuStrip;
    internal NotifyIcon TrayIcon => trayIcon;
    private bool? _wasOnAcPower = null;

    internal bool? WasOnAcPower
    {
        get => _wasOnAcPower;
        set => _wasOnAcPower = value;
    }

    private bool _wasGpuAwake;

    internal bool WasGpuAwake
    {
        get => _wasGpuAwake;
        set => _wasGpuAwake = value;
    }

    internal PowerProfile? LastOsdProfile { get; set; }
    private int _powerEventId = 0;

    private CancellationTokenSource _engineCts = new CancellationTokenSource();
    private int _lastAppliedCpuSpeed = -1;
    private int _lastAppliedGpuSpeed = -1;
    private RegisteredWaitHandle? _settingsSyncReg;
    private EventWaitHandle? _settingsSyncEvent;

    private readonly GlobalHotkeyManager _hotkeys = new();
    private readonly OsdForm _osd = new();
    private BacklightHotkeyWatcher? _backlightHotkeyWatcher;
    private string _lastCyclePowerHotkey = string.Empty;
    private KeyboardBacklightLevel? _lastSyncedBacklightLevel;
    private bool? _lastSyncedBacklightTimeout;
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
        try
        {
            appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Could not extract app icon: {ex.Message}", "Tray");
        }

        trayIcon = new NotifyIcon { Icon = appIcon, Visible = true, Text = "Nitrous" };
        trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowDashboard();
        };

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
                _settingsSyncEvent =
                    new EventWaitHandle(false, EventResetMode.AutoReset, Program.SettingsChangedEventName);
                _settingsSyncReg = ThreadPool.RegisterWaitForSingleObject(_settingsSyncEvent, (state, timedOut) =>
                {
                    LogManager.SyncSettings();
                    ReloadHotkeysIfChanged();
                    SyncBacklightSettingsIfChanged();
                    ReloadScenariosIfChanged();
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
            InitializeScenarioSyncBaselines();
            StartScenarioWatcher();
            _lastSyncedBacklightLevel = AcerWmiManager.GetBacklightLevel();
            _lastSyncedBacklightTimeout = AcerWmiManager.GetBacklightTimeout();

            try
            {
                _backlightHotkeyWatcher = new BacklightHotkeyWatcher();
                _backlightHotkeyWatcher.BacklightHotkeyPressed += (_, _) => OnHardwareBacklightHotkey();
                _backlightHotkeyWatcher.Start();
            }
            catch (Exception ex)
            {
                LogManager.Debug($"Backlight hotkey watcher init failed: {ex.Message}", "Tray");
            }

            _ = Task.Run(async () =>
            {
                // Clean up any legacy IFEO registry entries, dummy System32 files, or AppData scripts
                NitroKeyManager.PurgeLegacyIfeoIntegration();

                // Initial quick memory trim after JIT compilation
                await Task.Delay(2000);
                MemoryHelper.TrimWorkingSet();

                await Task.Delay(6000);
                ApplyPowerSettings(true);
                await ApplyScenarioAsync(ScenarioStore.GetActiveId(), "Boot");

                bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                if (ShouldApplyBootOc(isOnline))
                {
                    var bootProfile = AcerWmiManager.ResolvePowerProfileRequest(
                        (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance));
                    await _gpuManager.ApplyProfileOcAsync(bootProfile, isOnline);
                }

                // Final trim after all boot settings are applied
                MemoryHelper.TrimWorkingSet();
            });

            StartBackgroundEngine();
        }
    }


    private void ShowDashboard()
    {
        try
        {
            User32.AllowSetForegroundWindow(User32.ASFW_ANY);
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
        IntPtr existingHwnd = User32.FindWindow(null, "Nitrous Dashboard");
        if (existingHwnd != IntPtr.Zero)
        {
            User32.ShowWindow(existingHwnd, User32.SW_RESTORE);
            User32.SetForegroundWindow(existingHwnd);
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
                    try
                    {
                        uiProc.Dispose();
                    }
                    catch
                    {
                    }

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
            await ApplyScenarioAsync(ScenarioStore.GetActiveId(), "PowerChange");

            // Defer aggressive working set trimming until after OSD display & fade animations finish
            _ = Task.Delay(4000).ContinueWith(_ => MemoryHelper.TrimWorkingSet(), TaskScheduler.Default);
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        LogManager.Info($"Session ending (Reason: {e.Reason}). Triggering fan reset to Auto.", "Tray");
        TriggerExitFanReset();
    }

    internal static bool ShouldApplyBootOc(bool isOnline)
    {
        return isOnline && SettingsManager.GetInt("AutoApplyGpuClocks", 1) == 1;
    }

    internal void ApplyPowerSettings(bool isStartup = false, bool? isOnlineOverride = null)
    {
        bool isOnline = isOnlineOverride ?? (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);

        if (!isStartup && _wasOnAcPower.HasValue && _wasOnAcPower.Value == isOnline) return;
        _wasOnAcPower = isOnline;

        // Check for hardware drift on AC/DC transition (external writer: NitroSense / BIOS)
        var detectedMode = AcerWmiManager.GetActivePowerMode();
        var storedMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        string currentAcDcKey = isOnline ? "LastAcPowerMode" : "LastDcPowerMode";

        if (HardwareSafetyPolicy.HasDrift(detectedMode, storedMode))
        {
            var driftedMode = HardwareSafetyPolicy.ResolveDrift(detectedMode, storedMode);
            LogManager.Info($"Hardware power mode drift detected on AC/DC transition: {storedMode} -> {driftedMode}", "Tray");
            SettingsManager.Save("LastPowerMode", (int)driftedMode);
            SettingsManager.Save(currentAcDcKey, (int)driftedMode);
            TriggerProfileOsd(driftedMode);
        }

        if (SettingsManager.Get("AutoSwitch", 0) == 1)
        {
            var previousMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            string keyMode = isOnline ? "LastAcPowerMode" : "LastDcPowerMode";
            var activeMode = (PowerProfile)SettingsManager.Get(keyMode,
                (int)(isOnline ? PowerProfile.Performance : PowerProfile.Quiet));
            activeMode = AcerWmiManager.ResolvePowerProfileRequest(activeMode);

            SettingsManager.Save("LastPowerMode", (int)activeMode);

            string keyFan = isOnline ? "LastAcFanMode" : "LastDcFanMode";
            var activeFan = Enum.TryParse(SettingsManager.Get(keyFan, "Auto"), out FanProfile f) ? f : FanProfile.Auto;

            // The firmware owns the fans in Quiet/Turbo/Eco; never re-assert a stored
            // manual fan mode there or the saved state drifts from the EC.
            if (!AcerWmiManager.IsFanControlAllowed())
            {
                activeFan = FanProfile.Auto;
            }

            if (activeFan == FanProfile.Medium)
                _ = AcerWmiManager.ApplyPowerProfileAsync(activeMode,
                    SettingsManager.Get("CustomFanSpeedCpu", 50),
                    SettingsManager.Get("CustomFanSpeedGpu", 50));
            else
            {
                _ = AcerWmiManager.ApplyPowerProfileAsync(activeMode);
                _ = AcerWmiManager.SetFansAsync(activeFan);
            }

            SettingsManager.Save("LastFanMode", activeFan.ToString());

            if (!isStartup && activeMode != previousMode)
            {
                TriggerProfileOsd(activeMode);
            }
        }

        var savedMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        var currentProfile = AcerWmiManager.ResolvePowerProfileRequest(savedMode);
        if (currentProfile != savedMode)
        {
            LogManager.Warn($"{savedMode} downgraded to {currentProfile}: AC power or charge level not met", "Tray");
            SettingsManager.Save("LastPowerMode", (int)currentProfile);
        }

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

        _ = _gpuManager.ApplyProfileOcAsync(currentProfile, isOnline);
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

    internal void SyncBacklightSettingsIfChanged()
    {
        var level = AcerWmiManager.GetBacklightLevel() ?? KeyboardBacklightLevel.Level2;
        bool timeout = AcerWmiManager.GetBacklightTimeout();
        if (_lastSyncedBacklightLevel != level)
        {
            _lastSyncedBacklightLevel = level;
            _ = AcerWmiManager.ApplyPhysicalBacklightLevelAsync(level);
        }

        if (_lastSyncedBacklightTimeout != timeout)
        {
            _lastSyncedBacklightTimeout = timeout;
            _ = AcerWmiManager.SetBacklightTimeoutAsync(timeout);
        }
    }

    /// <summary>
    /// A hardware Fn+F11 press was observed via APGeEvent. The EC already stepped the
    /// hardware, so advance Nitrous' tracked level, reset the watchdog, and resync baselines
    /// so the settings-sync path does not re-apply a stale level over the new EC state.
    /// Runs on the WMI event thread; all callees are thread-safe.
    /// </summary>
    internal void OnHardwareBacklightHotkey()
    {
        try
        {
            AcerWmiManager.OnHardwareBacklightHotkey();
            _backlightWatchdog.Reset();
            _lastSyncedBacklightLevel = AcerWmiManager.GetBacklightLevel();
            _lastSyncedBacklightTimeout = AcerWmiManager.GetBacklightTimeout();
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Hardware backlight hotkey handling failed: {ex.Message}", "Tray");
        }
    }

    private void RegisterSavedHotkey(string settingKey, int id)
    {
        string saved = SettingsManager.Get(settingKey, "");
        if (string.IsNullOrEmpty(saved)) return;

        if (HotkeyFormatter.TryParse(saved, out uint mods, out uint key))
        {
            _hotkeys.Register(id, mods, key);
        }
        else
        {
            LogManager.Debug($"Failed registering hotkey ID {id} ('{saved}'): Invalid format", "Tray");
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
        bool overclockOk = AcerWmiManager.IsOverclockModeAvailable();
        var supported = AcerWmiManager.GetSupportedProfiles();

        PowerProfile nextProfile = HardwareSafetyPolicy.GetNextCycleProfile(currentProfile, supported, overclockOk, isOnline);

        var resolved = AcerWmiManager.ResolvePowerProfileRequest(nextProfile);
        if (resolved != nextProfile)
        {
            LogManager.Warn($"{nextProfile} downgraded to {resolved}: AC power or charge level not met", "Tray");
        }

        // Apply the new mode
        if (ScenarioStore.IsEnabled && !ScenarioStore.IsSuspended)
        {
            ScenarioStore.SetSuspended(true);
            LogManager.Info("Scenario suspended by manual power cycle hotkey", "Scenario");
        }
        SettingsManager.Save("LastPowerMode", (int)resolved);
        _ = AcerWmiManager.ApplyPowerProfileAsync(resolved);
        SettingsManager.Save(acDcKey, (int)resolved);

        if (SettingsManager.Get("ManageCpuPower", 0) == 1)
        {
            _ = CpuPowerManager.ApplyProfileLimitsAsync(resolved, isOnline);
        }

        _ = _gpuManager.ApplyProfileOcAsync(resolved, isOnline);
        TriggerProfileOsd(resolved);
    }

    public void TriggerProfileOsd(PowerProfile profile)
    {
        LastOsdProfile = profile;
        Color osdColor = OsdThemeHelper.GetAccent(profile);

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
        _backlightHotkeyWatcher?.Dispose();
        _backlightHotkeyWatcher = null;
        _foregroundWatcher?.Dispose();
        _foregroundWatcher = null;
        _scenarioDebounceTimer?.Dispose();
        _scenarioDebounceTimer = null;
        ClearSyncState();
        _appExitTimer?.Dispose();
        _appExitTimer = null;
        SystemEvents.SessionSwitch -= OnScenarioSessionSwitch;
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
                            try
                            {
                                p.Kill();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch
            {
            }
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
        Program.ReleaseAppMutex();
        Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restart") { UseShellExecute = true });
        Application.Exit();
    }

    private void Exit(object? sender, EventArgs e)
    {
        LogManager.Info("TrayApplication exit requested", "Tray");
        CleanupResources();
        Application.Exit();
    }
}
