using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Nitrous.Enums;
using Nitrous.Managers;
using Nitrous.Mvvm;
using Application = System.Windows.Application;

namespace Nitrous.Ui;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ActionDebouncer _fanDebouncer = new ActionDebouncer();
    private readonly NvidiaGpuManager _gpuManager;
    private CancellationTokenSource? _pollingCts;
    private PerformanceCounter? _cpuUsageCounter;

    internal PerformanceCounter? CpuUsageCounter => _cpuUsageCounter;
    internal void SetCpuUsageCounterForTesting(PerformanceCounter? counter) => _cpuUsageCounter = counter;

    public bool IsPollingActive => _pollingCts != null && !_pollingCts.IsCancellationRequested;

    private bool _chargeLimit;

    public bool ChargeLimit
    {
        get => _chargeLimit;
        set
        {
            if (SetProperty(ref _chargeLimit, value))
            {
                SettingsManager.Save("ChargeLimit", value ? 1 : 0);
                _ = BatteryManager.SetChargeLimitAsync(value);
            }
        }
    }

    private bool _autoSwitch;

    public bool AutoSwitch
    {
        get => _autoSwitch;
        set
        {
            if (SetProperty(ref _autoSwitch, value))
                SettingsManager.Save("AutoSwitch", value ? 1 : 0);
        }
    }

    private bool _refreshAutoSwitch;

    public bool RefreshAutoSwitch
    {
        get => _refreshAutoSwitch;
        set
        {
            if (SetProperty(ref _refreshAutoSwitch, value))
                SettingsManager.Save("RefreshAutoSwitch", value ? 1 : 0);
        }
    }

    private bool _runOnStartup;

    public bool RunOnStartup
    {
        get => _runOnStartup;
        set
        {
            if (SetProperty(ref _runOnStartup, value))
                StartupManager.ToggleStartupTask(value, Environment.ProcessPath ?? "");
        }
    }

    private bool _isDebuggingEnabled;

    public bool IsDebuggingEnabled
    {
        get => _isDebuggingEnabled;
        set
        {
            if (SetProperty(ref _isDebuggingEnabled, value))
            {
                SettingsManager.Save("Debugging", value);
                SettingsManager.Save("DebugLogging", value);
                LogManager.IsDebugEnabled = value;
                LogManager.Info($"Debugging mode toggled: {value}", "DashboardVM");
            }
        }
    }

    private bool _isNitroKeyIntegrated;

    public bool IsNitroKeyIntegrated
    {
        get => _isNitroKeyIntegrated;
        set
        {
            if (SetProperty(ref _isNitroKeyIntegrated, value))
            {
                NitroKeyManager.SetIntegration(value, Environment.ProcessPath ?? "");
                SettingsManager.Save("NitroKeyIntegrated", value ? 1 : 0);
            }
        }
    }

    public DashboardViewModel() : this(new NvidiaGpuManager())
    {
    }

    internal DashboardViewModel(NvidiaGpuManager gpuManager)
    {
        _gpuManager = gpuManager;

        // Initialize sub-domain states and commands
        InitializeFansAndProfiles();
        InitializeGpuTuning();
        InitializeServices();

        _autoApplyGpuClocks = SettingsManager.Get("AutoApplyGpuClocks", 1) == 1;

        // Initialize Settings State
        _chargeLimit = SettingsManager.Get("ChargeLimit", 0) == 1;
        _autoSwitch = SettingsManager.Get("AutoSwitch", 0) == 1;
        _refreshAutoSwitch = SettingsManager.Get("RefreshAutoSwitch", 0) == 1;
        _isDebuggingEnabled = SettingsManager.Get("Debugging", SettingsManager.Get("DebugLogging", false));
        LogManager.IsDebugEnabled = _isDebuggingEnabled;

        // Fast load cached profile to prevent UI blocking
        ActivePowerProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

        // Fast load cached or query Turbo support immediately to prevent UI flicker
        int cachedTurbo = SettingsManager.Get("IsTurboSupported", -1);
        if (cachedTurbo != -1)
        {
            _isTurboSupported = cachedTurbo == 1;
        }
        else
        {
            bool isTurbo = AcerWmiManager.IsTurboModeSupported();
            _isTurboSupported = isTurbo;
            SettingsManager.Save("IsTurboSupported", isTurbo ? 1 : 0);
        }

        int cachedEco = SettingsManager.Get("IsEcoSupported", -1);
        if (cachedEco != -1)
        {
            _isEcoSupported = cachedEco == 1;
        }
        else
        {
            bool isEco = AcerWmiManager.IsEcoModeSupported();
            _isEcoSupported = isEco;
            SettingsManager.Save("IsEcoSupported", isEco ? 1 : 0);
        }

        if (!_isTurboSupported && ActivePowerProfile == PowerProfile.Turbo)
        {
            ActivePowerProfile = PowerProfile.Performance;
        }

        if (!_isEcoSupported && ActivePowerProfile == PowerProfile.Eco)
        {
            ActivePowerProfile = PowerProfile.Balanced;
        }

        UpdateBatteryBadge();
        RefreshPowerModeAvailability();
        InitializeGpuTelemetryState();
        InitializeScenarios(); // after Turbo support resolution for mode list gating

        if (!IsTestHost())
        {
            Task.Run(() =>
            {
                var actualMode = AcerWmiManager.GetActivePowerMode();
                bool isTurbo = AcerWmiManager.IsTurboModeSupported();
                bool isEco = AcerWmiManager.IsEcoModeSupported();
                bool isTaskEnabled = StartupManager.CheckStartupTask();
                bool isNitroKeyEnabled = false;

                int core = 0, memory = 0;
                bool hasClocks = false;
                if (NvidiaGpuManager.IsGpuAwake())
                {
                    hasClocks = _gpuManager.IsValid && _gpuManager.GetClocks(out core, out memory);
                }

                if (!hasClocks)
                {
                    var (fallbackCore, fallbackMemory) = _gpuManager.ResolveProfileOffsets(ActivePowerProfile);
                    core = fallbackCore;
                    memory = fallbackMemory;
                    hasClocks = true;
                }

                // Push fast results back to the UI thread asynchronously
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (actualMode.HasValue)
                    {
                        ActivePowerProfile = actualMode.Value;
                    }

                    if (IsTurboSupported != isTurbo)
                    {
                        IsTurboSupported = isTurbo;
                        SettingsManager.Save("IsTurboSupported", isTurbo ? 1 : 0);
                    }

                    if (IsEcoSupported != isEco)
                    {
                        IsEcoSupported = isEco;
                        SettingsManager.Save("IsEcoSupported", isEco ? 1 : 0);
                    }

                    if (!isTurbo && ActivePowerProfile == PowerProfile.Turbo)
                    {
                        ActivePowerProfile = PowerProfile.Performance;
                    }

                    if (!isEco && ActivePowerProfile == PowerProfile.Eco)
                    {
                        ActivePowerProfile = PowerProfile.Balanced;
                    }

                    _runOnStartup = isTaskEnabled;
                    OnPropertyChanged(nameof(RunOnStartup));

                    _isNitroKeyIntegrated = isNitroKeyEnabled;
                    OnPropertyChanged(nameof(IsNitroKeyIntegrated));

                    if (hasClocks)
                    {
                        GpuCoreOffset = _gpuManager.ClampCoreOffset(core);
                        GpuMemoryOffset = _gpuManager.ClampMemoryOffset(memory);
                    }
                });

                try
                {
                    // Use 'Processor Information' and '% Processor Utility' to match Task Manager.
                    // This is notoriously slow on first access and must NOT be on the UI thread!
                    _cpuUsageCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
                }
                catch (Exception ex)
                {
                    LogManager.Debug($"PerformanceCounter init failed: {ex.Message}", "DashboardVM");
                }
            });
        }

        StartTelemetryPolling();
    }

    private void InitializeGpuTelemetryState()
    {
        bool isAwake = !IsTestHost() && NvidiaGpuManager.IsGpuAwake();
        _wasGpuAwake = isAwake;
        _gpuSleepHoldTicks = 0;
        if (isAwake)
        {
            _gpuSleepOverlayVisibility = Visibility.Collapsed;
            _gpuUsageText = "--%";
            _gpuGraphOpacity = 1.0;
        }
        else
        {
            _gpuSleepOverlayVisibility = Visibility.Visible;
            _gpuUsageText = "Sleep";
            _gpuGraphOpacity = 0.3;
            ClearDeepTelemetryUI();
        }
    }

    public void PausePolling()
    {
        _pollingCts?.Cancel();
    }

    public void ResumePolling()
    {
        bool isGpuAwake = !IsTestHost() && NvidiaGpuManager.IsGpuAwake();
        _wasGpuAwake = isGpuAwake;
        _gpuSleepHoldTicks = 0;
        if (!isGpuAwake)
        {
            _gpuSleepOverlayVisibility = Visibility.Visible;
            _gpuUsageText = "Sleep";
            _gpuGraphOpacity = 0.3;
            ClearDeepTelemetryUI();
            OnPropertyChanged(nameof(GpuSleepOverlayVisibility));
            OnPropertyChanged(nameof(GpuUsageText));
            OnPropertyChanged(nameof(GpuGraphOpacity));
        }
        else
        {
            _gpuSleepOverlayVisibility = Visibility.Collapsed;
            _gpuGraphOpacity = 1.0;
            if (GpuUsageText == "Sleep") GpuUsageText = "--%";
            OnPropertyChanged(nameof(GpuSleepOverlayVisibility));
            OnPropertyChanged(nameof(GpuUsageText));
            OnPropertyChanged(nameof(GpuGraphOpacity));
        }

        StartTelemetryPolling();
    }

    public void SyncSettings()
    {
        ReloadScenarios();

        int cachedTurbo = SettingsManager.Get("IsTurboSupported", -1);
        if (cachedTurbo != -1 && IsTurboSupported != (cachedTurbo == 1))
        {
            IsTurboSupported = cachedTurbo == 1;
        }

        int cachedEco = SettingsManager.Get("IsEcoSupported", -1);
        if (cachedEco != -1 && IsEcoSupported != (cachedEco == 1))
        {
            IsEcoSupported = cachedEco == 1;
        }

        var newPowerProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        if (!_isTurboSupported && newPowerProfile == PowerProfile.Turbo)
        {
            newPowerProfile = PowerProfile.Performance;
        }

        if (!_isEcoSupported && newPowerProfile == PowerProfile.Eco)
        {
            newPowerProfile = PowerProfile.Balanced;
        }

        if (_activePowerProfile != newPowerProfile)
        {
            _activePowerProfile = newPowerProfile;
            OnPropertyChanged(nameof(ActivePowerProfile));

            var (syncedCore, syncedMemory) = _gpuManager.ResolveProfileOffsets(newPowerProfile);
            GpuCoreOffset = syncedCore;
            GpuMemoryOffset = syncedMemory;
        }

        // Expert Mode or the active mode may have changed behind our back.
        OnPropertyChanged(nameof(IsFanControlLocked));
        OnPropertyChanged(nameof(IsFanControlEnabled));
        OnPropertyChanged(nameof(FanLockVisibility));
        OnPropertyChanged(nameof(FanLockText));
        RefreshPowerModeAvailability();
        RefreshWhisperState();

        var activeFan = Enum.TryParse(SettingsManager.Get("LastFanMode", "Auto"), out FanProfile f)
            ? f
            : FanProfile.Auto;
        SyncActiveFanMode(activeFan);

        var newCpu = SettingsManager.Get("CustomFanSpeedCpu", 50);
        if (_cpuFanSpeed != newCpu)
        {
            _cpuFanSpeed = newCpu;
            OnPropertyChanged(nameof(CpuFanSpeed));
        }

        var newGpu = SettingsManager.Get("CustomFanSpeedGpu", 50);
        if (_gpuFanSpeed != newGpu)
        {
            _gpuFanSpeed = newGpu;
            OnPropertyChanged(nameof(GpuFanSpeed));
        }

        var newUnified = SettingsManager.Get("UnifiedFans", 0) == 1;
        if (_isUnifiedFans != newUnified)
        {
            _isUnifiedFans = newUnified;
            OnPropertyChanged(nameof(IsUnifiedFans));
        }

        var newCurve = SettingsManager.Get("IsCurveModeEnabled", 0) == 1;
        if (_isCurveModeEnabled != newCurve)
        {
            _isCurveModeEnabled = newCurve;
            OnPropertyChanged(nameof(IsCurveModeEnabled));
            OnPropertyChanged(nameof(IsManualSliderEnabled));
            OnPropertyChanged(nameof(FanReadViewVisibility));
            OnPropertyChanged(nameof(FanWriteViewVisibility));
            OnPropertyChanged(nameof(CustomFanHeaderText));
            OnPropertyChanged(nameof(CustomFanHeaderColor));
            OnPropertyChanged(nameof(FanModeSubtext));
            OnPropertyChanged(nameof(FanModeSubtextColor));
            OnPropertyChanged(nameof(SliderDisabledTooltip));
        }

        var newRefresh = (RefreshProfile)SettingsManager.Get("RefreshMode", (int)RefreshProfile.Auto);
        if (_activeRefreshProfile != newRefresh)
        {
            _activeRefreshProfile = newRefresh;
            OnPropertyChanged(nameof(ActiveRefreshProfile));
            OnPropertyChanged(nameof(RefreshBadgeText));
        }

        var newManageCpu = SettingsManager.Get("ManageCpuPower", 0) == 1;
        if (_manageCpuPower != newManageCpu)
        {
            _manageCpuPower = newManageCpu;
            OnPropertyChanged(nameof(ManageCpuPower));
        }

        var newCharge = SettingsManager.Get("ChargeLimit", 0) == 1;
        if (_chargeLimit != newCharge)
        {
            _chargeLimit = newCharge;
            OnPropertyChanged(nameof(ChargeLimit));
        }

        var newAuto = SettingsManager.Get("AutoSwitch", 0) == 1;
        if (_autoSwitch != newAuto)
        {
            _autoSwitch = newAuto;
            OnPropertyChanged(nameof(AutoSwitch));
        }

        var newRefAuto = SettingsManager.Get("RefreshAutoSwitch", 0) == 1;
        if (_refreshAutoSwitch != newRefAuto)
        {
            _refreshAutoSwitch = newRefAuto;
            OnPropertyChanged(nameof(RefreshAutoSwitch));
        }

        var newAutoApply = SettingsManager.Get("AutoApplyGpuClocks", 1) == 1;
        if (_autoApplyGpuClocks != newAutoApply)
        {
            _autoApplyGpuClocks = newAutoApply;
            OnPropertyChanged(nameof(AutoApplyGpuClocks));
        }


        var newDebugging = SettingsManager.Get("Debugging", SettingsManager.Get("DebugLogging", false));
        if (_isDebuggingEnabled != newDebugging)
        {
            _isDebuggingEnabled = newDebugging;
            LogManager.IsDebugEnabled = newDebugging;
            OnPropertyChanged(nameof(IsDebuggingEnabled));
        }
    }

    public void Dispose()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _fanDebouncer.Dispose();
        _gpuManager.Dispose();
        try
        {
            _cpuUsageCounter?.Dispose();
        }
        catch (Exception ex)
        {
            LogManager.Debug($"PerformanceCounter dispose failed: {ex.Message}", "DashboardVM");
        }
        _cpuUsageCounter = null;
    }

    private static bool IsTestHost()
    {
        string procName = Process.GetCurrentProcess().ProcessName;
        return procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
               procName.Contains("vstest", StringComparison.OrdinalIgnoreCase);
    }
}
