using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Hooks;
using Nitrous.Managers;

namespace Nitrous.Ui;

public enum ScenarioFanAction
{
    PresetMode = 0,
    FixedSpeeds = 1,
    Curve = 2,
}

public readonly record struct ScenarioApplyPlan(
    PowerProfile EffectiveMode,
    ScenarioFanAction FanAction,
    FanProfile FanMode,
    int CustomFanSpeedCpu,
    int CustomFanSpeedGpu,
    string FanCurvePreset);

public partial class TrayApplication : ApplicationContext
{
    private string _lastScenarioActiveId = string.Empty;
    private int _lastScenarioApplyRequest;
    private string _lastAppliedScenarioSignature = string.Empty;

    private ToolStripMenuItem? _scenarioMenu;

    private ForegroundAppWatcher? _foregroundWatcher;
    private System.Threading.Timer? _scenarioDebounceTimer;
    private string? _pendingForegroundExe;
    private string? _lastForegroundExe;
    private string? _suppressedForegroundExe;
    private bool _scenarioSessionLocked;

    private string? _appSyncActiveId;
    private readonly List<(string Exe, string ProfileId)> _syncContributors = [];
    private readonly object _syncStateLock = new(); // hook, debounce/exit timers, settings-sync threads
    private readonly List<Process> _watchedAppProcesses = [];
    private System.Threading.Timer? _appExitTimer;

    /// <summary>Pure apply-plan seam: decides mode + fan action without touching hardware.</summary>
    internal static ScenarioApplyPlan BuildScenarioApplyPlan(ScenarioProfile profile, bool isAcOnline, IReadOnlyList<PowerProfile> supportedProfiles)
    {
        var effective = ScenarioResolver.EffectiveMode(profile, isAcOnline, supportedProfiles);
        var fanAction = profile.FanCurvePreset != "Auto"
            ? ScenarioFanAction.Curve
            : profile.FanMode == FanProfile.Medium
                ? ScenarioFanAction.FixedSpeeds
                : ScenarioFanAction.PresetMode;
        return new ScenarioApplyPlan(
            effective, fanAction, profile.FanMode,
            profile.CustomFanSpeedCpu, profile.CustomFanSpeedGpu, profile.FanCurvePreset);
    }

    internal static ScenarioApplyPlan BuildScenarioApplyPlan(ScenarioProfile profile, bool isAcOnline, bool turboSupported) =>
        BuildScenarioApplyPlan(profile, isAcOnline, turboSupported
            ? [PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo]
            : [PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance]);

    internal void InitializeScenarioSyncBaselines()
    {
        try
        {
            ScenarioStore.SeedIfAbsent();
            _lastScenarioActiveId = ScenarioStore.GetActiveId();
            _lastScenarioApplyRequest = ScenarioStore.GetApplyRequest();
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Scenario baseline init failed: {ex.Message}", "Scenario");
        }
    }

    private void RebuildScenarioMenu()
    {
        if (_scenarioMenu == null) return;
        _scenarioMenu.DropDownItems.Clear();

        if (!ScenarioStore.IsEnabled)
        {
            _scenarioMenu.DropDownItems.Add(new ToolStripMenuItem("Scenarios Off (enable in Dashboard)") { Enabled = false });
            return;
        }

        string activeId = ScenarioStore.GetActiveId();
        foreach (var profile in ScenarioStore.LoadProfiles())
        {
            string id = profile.Id;
            string name = profile.Name;
            var item = new ToolStripMenuItem(name, null, async (_, _) =>
            {
                try
                {
                    ScenarioStore.SetSelectedId(id);
                    ScenarioStore.SetActiveId(id);
                    await ApplyScenarioAsync(id, "TrayMenu");
                }
                catch (Exception ex)
                {
                    LogManager.Warn($"Tray scenario apply failed: {ex.Message}", "Scenario");
                }
            })
            {
                Checked = id == activeId,
            };
            _scenarioMenu.DropDownItems.Add(item);
        }
    }

    private void TriggerScenarioOsd(ScenarioProfile profile, PowerProfile effectiveMode)
    {
        _osd.ShowProfile(profile.Name, OsdThemeHelper.GetAccent(effectiveMode), effectiveMode);
    }

    internal void StartScenarioWatcher()
    {
        try
        {
            _foregroundWatcher = new ForegroundAppWatcher();
            _foregroundWatcher.ForegroundExeChanged += OnForegroundExeChanged;
            _scenarioDebounceTimer = new System.Threading.Timer(OnScenarioDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
            SystemEvents.SessionSwitch += OnScenarioSessionSwitch;
            _foregroundWatcher.Start();
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Scenario watcher start failed: {ex.Message}", "AppSync");
        }
    }

    private void OnScenarioSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _scenarioSessionLocked = true;
            _pendingForegroundExe = null;
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _scenarioSessionLocked = false;
            _foregroundWatcher?.RaiseCurrentForeground();
        }
    }

    private void OnForegroundExeChanged(object? sender, string? exeFileName)
    {
        // Null = own process or unresolvable: ignored entirely, never a no-match.
        if (exeFileName == null || _scenarioSessionLocked) return;

        // Manual apply suppresses App Sync re-match for the same foreground exe
        // until focus moves to a different exe. Prevents watcher fighting the user.
        if (_suppressedForegroundExe != null)
        {
            if (string.Equals(exeFileName, _suppressedForegroundExe, StringComparison.OrdinalIgnoreCase)) return;
            _suppressedForegroundExe = null;
        }

        if (string.Equals(exeFileName, _lastForegroundExe, StringComparison.OrdinalIgnoreCase)) return;

        _pendingForegroundExe = exeFileName;
        _scenarioDebounceTimer?.Change(750, Timeout.Infinite);
    }

    private async void OnScenarioDebounceElapsed(object? state)
    {
        try
        {
            string? exe = _pendingForegroundExe;
            _pendingForegroundExe = null;
            if (exe == null || _scenarioSessionLocked || !ScenarioStore.IsEnabled) return;
            _lastForegroundExe = exe;

            var profiles = ScenarioStore.LoadProfiles();

            // Only an exe explicitly linked to an enabled profile can become a
            // combine contributor. Unmatched foreground apps must never be
            // attributed to the currently active profile.
            var matchedProfile = ScenarioResolver.FindLinkedProfile(profiles, exe);
            if (matchedProfile == null) return;

            AddSyncContributor(exe, matchedProfile.Id);

            bool onlyMatchedContributor;
            lock (_syncStateLock)
            {
                onlyMatchedContributor = _syncContributors.All(c => c.ProfileId == matchedProfile.Id);
            }

            bool alreadyApplied = matchedProfile.Id == ScenarioStore.GetActiveId()
                                  && _appSyncActiveId == matchedProfile.Id
                                  && onlyMatchedContributor;
            if (alreadyApplied)
            {
                WatchContributors();
                return;
            }

            await RecomputeSyncContributorsAsync();
        }
        catch (Exception ex)
        {
            LogManager.Warn($"App Sync switch failed: {ex.Message}", "AppSync");
        }
    }

    private void AddSyncContributor(string exeFileName, string profileId)
    {
        lock (_syncStateLock)
        {
            _syncContributors.RemoveAll(c => string.Equals(c.Exe, exeFileName, StringComparison.OrdinalIgnoreCase));
            _syncContributors.Add((exeFileName, profileId));
        }
    }

    /// <summary>
    /// Rebuilds the effective App Sync state from every linked app still running:
    /// one contributor applies its profile; several merge per setting (highest wins
    /// per group, see ScenarioCombiner); none reverts to the selected profile.
    /// Event-driven via Process.Exited; no polling.
    /// </summary>
    private async Task RecomputeSyncContributorsAsync()
    {
        var profiles = ScenarioStore.LoadProfiles();

        List<(string Exe, string ProfileId)> contributors;
        string? syncActiveId;
        lock (_syncStateLock)
        {
            // Rare event path (match/exit only): process checks under the gate are fine.
            _syncContributors.RemoveAll(c => !IsExeRunning(c.Exe));
            contributors = [.. _syncContributors];
            syncActiveId = _appSyncActiveId;
        }

        if (contributors.Count == 0)
        {
            // Discover a linked app that runs but never took focus.
            var discovered = ScenarioResolver.FindRunningLinkedProfile(profiles, syncActiveId, IsExeRunning);
            if (discovered != null)
            {
                AddSyncContributor(discovered.Value.ExeFileName, discovered.Value.Profile.Id);
                lock (_syncStateLock) { contributors = [.. _syncContributors]; }
            }
        }

        if (contributors.Count == 0)
        {
            var exitResolution = ScenarioResolver.ResolveAppExit(
                profiles, ScenarioStore.GetActiveId(), ScenarioStore.GetSelectedId(), syncActiveId);
            ClearSyncState();
            if (exitResolution.Reason == ScenarioSwitchReason.Revert)
            {
                await ApplyScenarioAsync(exitResolution.TargetActiveId, "AppSync:Exit");
            }
            return;
        }

        var contributorProfiles = new List<ScenarioProfile>();
        foreach (var contributor in contributors)
        {
            var profile = ScenarioResolver.FindProfile(profiles, contributor.ProfileId);
            if (profile != null && contributorProfiles.All(p => p.Id != profile.Id))
            {
                contributorProfiles.Add(profile);
            }
        }

        if (contributorProfiles.Count == 0)
        {
            ClearSyncState();
            return;
        }

        var effective = contributorProfiles.Count == 1
            ? contributorProfiles[0]
            : ScenarioCombiner.Combine(contributorProfiles);
        lock (_syncStateLock) { _appSyncActiveId = effective.Id; }

        await ApplyScenarioProfileAsync(effective, contributorProfiles.Count == 1 ? "AppSync:Match" : "AppSync:Combine");
        WatchContributors();
    }

    private void WatchContributors()
    {
        ClearProcessWatches();
        _appExitTimer ??= new System.Threading.Timer(OnAppExitCheck, null, Timeout.Infinite, Timeout.Infinite);

        List<string> contributorExes;
        lock (_syncStateLock)
        {
            contributorExes = _syncContributors.Select(c => c.Exe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        foreach (string exe in contributorExes)
        {
            string processName = Path.GetFileNameWithoutExtension(exe);
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        process.EnableRaisingEvents = true;
                        process.Exited += OnWatchedAppExited;
                        _watchedAppProcesses.Add(process);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Debug($"App Sync watch failed for pid {process.Id}: {ex.Message}", "AppSync");
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Warn($"App Sync watch setup failed: {ex.Message}", "AppSync");
            }
        }
    }

    private void OnWatchedAppExited(object? sender, EventArgs e)
    {
        // Short delay absorbs exit/restart races before the still-running check.
        _appExitTimer?.Change(1000, Timeout.Infinite);
    }

    private static bool IsExeRunning(string exeFileName)
    {
        try
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeFileName));
            bool any = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            return any;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"App Sync running check failed for {exeFileName}: {ex.Message}", "AppSync");
            return false;
        }
    }

    private async void OnAppExitCheck(object? state)
    {
        try
        {
            bool hasContributors;
            lock (_syncStateLock) { hasContributors = _syncContributors.Count > 0; }
            if (!hasContributors || !ScenarioStore.IsEnabled) return;
            await RecomputeSyncContributorsAsync();
        }
        catch (Exception ex)
        {
            LogManager.Warn($"App Sync exit check failed: {ex.Message}", "AppSync");
        }
    }

    private void ClearProcessWatches()
    {
        foreach (var process in _watchedAppProcesses)
        {
            try
            {
                process.Exited -= OnWatchedAppExited;
                process.Dispose();
            }
            catch (Exception ex)
            {
                LogManager.Debug($"App Sync watch cleanup failed: {ex.Message}", "AppSync");
            }
        }
        _watchedAppProcesses.Clear();
    }

    internal void ClearSyncState()
    {
        ClearProcessWatches();
        lock (_syncStateLock)
        {
            _syncContributors.Clear();
            _appSyncActiveId = null;
        }
    }

    /// <summary>Settings-sync entry: applies only when active profile or apply nonce changed.</summary>
    internal void ReloadScenariosIfChanged()
    {
        try
        {
            if (!ScenarioStore.IsEnabled) return;

            string activeId = ScenarioStore.GetActiveId();
            int request = ScenarioStore.GetApplyRequest();
            if (activeId == _lastScenarioActiveId && request == _lastScenarioApplyRequest) return;

            _lastScenarioActiveId = activeId;
            _lastScenarioApplyRequest = request;
            _ = ApplyScenarioAsync(activeId, "Sync");
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Scenario sync failed: {ex.Message}", "Scenario");
        }
    }

    /// <summary>Keyboard slice of a Scenario: timeout + RGB effect/zone colors. Tray only.</summary>
    private void ApplyScenarioKeyboard(ScenarioProfile profile, bool previousTimeout)
    {
        try
        {
            if (previousTimeout != profile.BacklightTimeout)
            {
                // Update baseline first so the settings-sync path does not re-apply.
                _lastSyncedBacklightTimeout = profile.BacklightTimeout;
                _ = AcerWmiManager.SetBacklightTimeoutAsync(profile.BacklightTimeout);
            }

            if (!AcerKeyboard.IsAvailable()) return;

            var colors = new List<KeyboardColor>(4);
            foreach (string hex in profile.ZoneColors) colors.Add(KeyboardColor.FromHex(hex));

            bool applied = profile.LightingEffect == KeyboardEffect.Static || !AcerKeyboard.SupportsEffects
                ? AcerKeyboard.SetZoneColors(colors, 100)
                : AcerKeyboard.SetEffect(profile.LightingEffect, 100, 4, leftwards: false, colors[0]);

            if (!applied) LogManager.Debug($"Scenario keyboard lighting not applied for {profile.Name}", "Scenario");
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Scenario keyboard apply failed: {ex.Message}", "Scenario");
        }
    }

    /// <summary>
    /// Sole hardware applier for Scenarios. Dashboard saves + signals; this runs in Tray only.
    /// </summary>
    internal async Task ApplyScenarioAsync(string? targetId, string reason)
    {
        var profiles = ScenarioStore.LoadProfiles();
        if (profiles.Count == 0) return;

        var profile = ScenarioResolver.FindProfile(profiles, targetId)
                      ?? ScenarioResolver.FindProfile(profiles, ScenarioStore.GetActiveId())
                      ?? profiles[0];
        await ApplyScenarioProfileAsync(profile, reason);
    }

    private async Task ApplyScenarioProfileAsync(ScenarioProfile profileToApply, string reason)
    {
        try
        {
            if (!ScenarioStore.IsEnabled) return;

            if (ScenarioStore.IsSuspended)
            {
                // User overrode profile manually in Dashboard/Tray: their choice
                // wins until the next explicit Scenario apply.
                if (reason == "PowerChange") return;
                ScenarioStore.SetSuspended(false);
                LogManager.Info("Scenario suspension cleared by explicit apply", "Scenario");
            }

            var profile = profileToApply;

            if (reason == "Sync" || reason == "TrayMenu")
            {
                // Manual apply wins over App Sync; stop holding the sync profile.
                ClearSyncState();
                _suppressedForegroundExe = _lastForegroundExe;
            }

            bool isAc = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
            var plan = BuildScenarioApplyPlan(profile, isAc, AcerWmiManager.GetSupportedProfiles());

            string signature = $"{profile.Id}|{plan.EffectiveMode}|{plan.FanAction}|{plan.FanMode}|{plan.CustomFanSpeedCpu}|{plan.CustomFanSpeedGpu}|{plan.FanCurvePreset}|{isAc}|{profile.BacklightTimeout}|{profile.LightingEffect}|{string.Join(",", profile.ZoneColors)}";
            bool changed = signature != _lastAppliedScenarioSignature;
            _lastAppliedScenarioSignature = signature;

            ScenarioStore.SetActiveId(profile.Id);
            _lastScenarioActiveId = profile.Id;
            // Capture before write-through overwrites the saved timeout value.
            bool previousTimeout = AcerWmiManager.GetBacklightTimeout();
            ScenarioStore.WriteThroughLegacy(profile, plan.EffectiveMode, isAc);

            await AcerWmiManager.ApplyPowerProfileAsync(plan.EffectiveMode).ConfigureAwait(false);

            switch (plan.FanAction)
            {
                case ScenarioFanAction.FixedSpeeds:
                    await AcerWmiManager.SetFansAsync(FanProfile.Medium, plan.CustomFanSpeedCpu, plan.CustomFanSpeedGpu).ConfigureAwait(false);
                    break;
                case ScenarioFanAction.PresetMode:
                    await AcerWmiManager.SetFansAsync(plan.FanMode).ConfigureAwait(false);
                    break;
                case ScenarioFanAction.Curve:
                    // ThermalEngine owns curve fans per tick via LastFanMode/ActiveFanCurvePreset keys.
                    break;
            }

            if (SettingsManager.Get("ManageCpuPower", 0) == 1)
            {
                _ = CpuPowerManager.ApplyProfileLimitsAsync(plan.EffectiveMode, isAc);
            }

            // GPU offsets stay keyed by PowerProfile; existing AC/awake gating inside.
            _ = _gpuManager.ApplyProfileOcAsync(plan.EffectiveMode, isAc);

            ApplyScenarioKeyboard(profile, previousTimeout);

            if (changed)
            {
                LogManager.Info($"Scenario applied: {profile.Name} ({reason}), mode {plan.EffectiveMode}", "Scenario");
                if (reason != "Boot" && reason != "PowerChange")
                {
                    TriggerScenarioOsd(profile, plan.EffectiveMode);
                }
            }
            else
            {
                LogManager.Debug($"Scenario re-applied without change: {profile.Name} ({reason})", "Scenario");
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"ApplyScenario failed: {ex.Message}", "Scenario");
        }
    }
}
