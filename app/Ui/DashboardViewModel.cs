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
                _ = AcerWmiManager.SetChargeLimitAsync(value);
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

        _deepGpuTelemetry = SettingsManager.Get("DeepGpuTelemetry", 1) == 1;
        if (!_deepGpuTelemetry)
        {
            ClearDeepTelemetryUI();
        }

        // Initialize Settings State
        _chargeLimit = SettingsManager.Get("ChargeLimit", 0) == 1;
        _autoSwitch = SettingsManager.Get("AutoSwitch", 0) == 1;
        _refreshAutoSwitch = SettingsManager.Get("RefreshAutoSwitch", 0) == 1;
        _isDebuggingEnabled = SettingsManager.Get("Debugging", SettingsManager.Get("DebugLogging", false));

        // Fast load cached profile to prevent UI blocking
        ActivePowerProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

        if (!IsTestHost())
        {
            Task.Run(() =>
            {
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

                var actualMode = AcerWmiManager.GetActivePowerMode();

                bool isTurbo = AcerWmiManager.IsTurboModeSupported();
                bool isTaskEnabled = StartupManager.CheckStartupTask();
                bool isNitroKeyEnabled = false;

                int core = 0, memory = 0;
                bool hasClocks = _gpuManager.IsValid && _gpuManager.GetClocks(out core, out memory);

                // Push results back to the UI thread asynchronously
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (actualMode.HasValue)
                    {
                        ActivePowerProfile = actualMode.Value;
                    }

                    IsTurboSupported = isTurbo;

                    _runOnStartup = isTaskEnabled;
                    OnPropertyChanged(nameof(RunOnStartup));

                    _isNitroKeyIntegrated = isNitroKeyEnabled;
                    OnPropertyChanged(nameof(IsNitroKeyIntegrated));

                    if (hasClocks)
                    {
                        GpuCoreOffset = core;
                        GpuMemoryOffset = memory;
                    }
                });
            });
        }

        StartTelemetryPolling();
    }

    public void PausePolling()
    {
        _pollingCts?.Cancel();
    }

    public void ResumePolling()
    {
        StartTelemetryPolling();
    }

    public void SyncSettings()
    {
        var newPowerProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        if (_activePowerProfile != newPowerProfile)
        {
            _activePowerProfile = newPowerProfile;
            OnPropertyChanged(nameof(ActivePowerProfile));

            if (_gpuManager.GetClocks(out int c, out int m))
            {
                GpuCoreOffset = c;
                GpuMemoryOffset = m;
            }
            else
            {
                GpuCoreOffset = SettingsManager.Get($"GpuCore_{newPowerProfile}", _gpuManager.GetDefaultCore(newPowerProfile));
                GpuMemoryOffset = SettingsManager.Get($"GpuMemory_{newPowerProfile}", _gpuManager.GetDefaultMemory(newPowerProfile));
            }
        }

        var activeFan = Enum.TryParse(SettingsManager.Get("LastFanMode", "Auto"), out FanProfile f)
            ? f
            : FanProfile.Auto;
        var newCustomFan = activeFan == FanProfile.Medium;
        if (_isCustomFanEnabled != newCustomFan)
        {
            _isCustomFanEnabled = newCustomFan;
            OnPropertyChanged(nameof(IsCustomFanEnabled));
            OnPropertyChanged(nameof(CustomFanOpacity));
            OnPropertyChanged(nameof(IsManualSliderEnabled));
            OnPropertyChanged(nameof(CustomFanHeaderText));
            OnPropertyChanged(nameof(CustomFanHeaderColor));
            OnPropertyChanged(nameof(SliderDisabledTooltip));
        }

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

        var newUnified = SettingsManager.Get("UnifiedFans", 1) == 1;
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
            OnPropertyChanged(nameof(CustomFanHeaderText));
            OnPropertyChanged(nameof(CustomFanHeaderColor));
            OnPropertyChanged(nameof(FanModeSubtext));
            OnPropertyChanged(nameof(FanModeSubtextColor));
            OnPropertyChanged(nameof(SliderDisabledTooltip));
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

        var newDeepGpu = SettingsManager.Get("DeepGpuTelemetry", 1) == 1;
        if (_deepGpuTelemetry != newDeepGpu)
        {
            _deepGpuTelemetry = newDeepGpu;
            OnPropertyChanged(nameof(DeepGpuTelemetry));
            if (!_deepGpuTelemetry) ClearDeepTelemetryUI();
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
