using System.Diagnostics;
using System.Windows.Input;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Mvvm;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using SystemInformation = System.Windows.Forms.SystemInformation;

namespace Nitrous.Ui;

public class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ActionDebouncer _fanDebouncer = new ActionDebouncer();
    private readonly NvidiaGpuManager _gpuManager = new();
    private CancellationTokenSource? _pollingCts;

    private string _cpuTempText = "--°C";

    public string CpuTempText
    {
        get => _cpuTempText;
        set => SetProperty(ref _cpuTempText, value);
    }

    private string _cpuTempColor = "White";

    public string CpuTempColor
    {
        get => _cpuTempColor;
        set => SetProperty(ref _cpuTempColor, value);
    }

    private string _cpuRpmText = "-- RPM";

    public string CpuRpmText
    {
        get => _cpuRpmText;
        set => SetProperty(ref _cpuRpmText, value);
    }

    private string _gpuTempText = "--°C";

    public string GpuTempText
    {
        get => _gpuTempText;
        set => SetProperty(ref _gpuTempText, value);
    }

    private string _gpuRpmText = "-- RPM";

    public string GpuRpmText
    {
        get => _gpuRpmText;
        set => SetProperty(ref _gpuRpmText, value);
    }

    private string _gpuTempColor = "White";

    public string GpuTempColor
    {
        get => _gpuTempColor;
        set => SetProperty(ref _gpuTempColor, value);
    }

    private string _applyBtnText = "APPLY";

    public string ApplyBtnText
    {
        get => _applyBtnText;
        set => SetProperty(ref _applyBtnText, value);
    }

    private string _applyBtnColor = "#B388FF";

    public string ApplyBtnColor
    {
        get => _applyBtnColor;
        set => SetProperty(ref _applyBtnColor, value);
    }

    private int _gpuCoreOffset;

    public int GpuCoreOffset
    {
        get => _gpuCoreOffset;
        set => SetProperty(ref _gpuCoreOffset, value);
    }

    private int _gpuMemoryOffset;

    public int GpuMemoryOffset
    {
        get => _gpuMemoryOffset;
        set => SetProperty(ref _gpuMemoryOffset, value);
    }

    private string _gpuNameText = "NVIDIA GPU";

    public string GpuNameText
    {
        get => _gpuNameText;
        set => SetProperty(ref _gpuNameText, value);
    }

    private string _gpuArchText = "";

    public string GpuArchText
    {
        get => _gpuArchText;
        set => SetProperty(ref _gpuArchText, value);
    }

    private bool _manageCpuPower;

    public bool ManageCpuPower
    {
        get => _manageCpuPower;
        set
        {
            if (SetProperty(ref _manageCpuPower, value))
            {
                SettingsManager.Save("ManageCpuPower", value ? 1 : 0);
                bool isOnline = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;

                if (value)
                {
                    _ = CpuPowerManager.ApplyProfileLimitsAsync(ActivePowerProfile, isOnline);
                }
                else
                {
                    _ = CpuPowerManager.RestoreDefaultsAsync(isOnline);
                }
            }
        }
    }

    private string _gpuLoadText = "0%";

    public string GpuLoadText
    {
        get => _gpuLoadText;
        set => SetProperty(ref _gpuLoadText, value);
    }

    private string _gpuVramText = "0 / 0 MB";

    public string GpuVramText
    {
        get => _gpuVramText;
        set => SetProperty(ref _gpuVramText, value);
    }

    private string _gpuDeepTempText = "0 C";

    public string GpuDeepTempText
    {
        get => _gpuDeepTempText;
        set => SetProperty(ref _gpuDeepTempText, value);
    }

    private string _gpuPStateText = "P0";

    public string GpuPStateText
    {
        get => _gpuPStateText;
        set => SetProperty(ref _gpuPStateText, value);
    }

    private string _gpuLoadColor = "#B388FF";

    public string GpuLoadColor
    {
        get => _gpuLoadColor;
        set => SetProperty(ref _gpuLoadColor, value);
    }

    private string _gpuDeepTempColor = "#B388FF";

    public string GpuDeepTempColor
    {
        get => _gpuDeepTempColor;
        set => SetProperty(ref _gpuDeepTempColor, value);
    }

    private string _gpuCoreClockText = "0 MHz";

    public string GpuCoreClockText
    {
        get => _gpuCoreClockText;
        set => SetProperty(ref _gpuCoreClockText, value);
    }

    private string _gpuMemClockText = "0 MHz";

    public string GpuMemClockText
    {
        get => _gpuMemClockText;
        set => SetProperty(ref _gpuMemClockText, value);
    }

    private string _gpuPowerText = "0.0 W";

    public string GpuPowerText
    {
        get => _gpuPowerText;
        set => SetProperty(ref _gpuPowerText, value);
    }

    private bool _isTurboSupported = true;

    public bool IsTurboSupported
    {
        get => _isTurboSupported;
        set => SetProperty(ref _isTurboSupported, value);
    }

    private PowerProfile _activePowerProfile;

    public PowerProfile ActivePowerProfile
    {
        get => _activePowerProfile;
        set => SetProperty(ref _activePowerProfile, value);
    }

    private bool _isCurveModeEnabled;

    public bool IsCurveModeEnabled
    {
        get => _isCurveModeEnabled;
        set
        {
            if (SetProperty(ref _isCurveModeEnabled, value))
            {
                SettingsManager.Save("IsCurveModeEnabled", value ? 1 : 0);
                SettingsManager.Save("FanCurveVersion", DateTime.UtcNow.Ticks);
                OnPropertyChanged(nameof(IsManualSliderEnabled));
                OnPropertyChanged(nameof(CustomFanHeaderText));
                OnPropertyChanged(nameof(CustomFanHeaderColor));
                OnPropertyChanged(nameof(FanModeSubtext));
                OnPropertyChanged(nameof(FanModeSubtextColor));
                OnPropertyChanged(nameof(SliderDisabledTooltip));

                if (value && IsCustomFanEnabled)
                {
                    ApplyCurrentFanCurve();
                }
            }
        }
    }

    public bool IsManualSliderEnabled => IsCustomFanEnabled && !IsCurveModeEnabled;
    public string CustomFanHeaderText => IsCurveModeEnabled ? "CUSTOM FANS · CURVE" : "CUSTOM FANS · FIXED";
    public string CustomFanHeaderColor => IsCurveModeEnabled ? "#34C759" : "#888890";
    public string FanModeSubtext => IsCurveModeEnabled ? "CURVE ACTIVE" : "MANUAL (FIXED)";
    public string FanModeSubtextColor => IsCurveModeEnabled ? "#34C759" : "#B388FF";

    public string SliderDisabledTooltip => IsCurveModeEnabled
        ? "Manual sliders are disabled while Fan Curve is active. Adjust your curve in CURVE EDITOR."
        : "Adjust fixed fan percentage";

    public DashboardViewModel()
    {
        // Initialize Fan State
        _cpuFanSpeed = SettingsManager.Get("CustomFanSpeedCpu", 50);
        _gpuFanSpeed = SettingsManager.Get("CustomFanSpeedGpu", 50);
        _isUnifiedFans = SettingsManager.Get("UnifiedFans", 1) == 1;
        _isCurveModeEnabled = SettingsManager.Get("IsCurveModeEnabled", 0) == 1;
        _manageCpuPower = SettingsManager.Get("ManageCpuPower", 0) == 1;

        _deepGpuTelemetry = SettingsManager.Get("DeepGpuTelemetry", 1) == 1;
        if (!_deepGpuTelemetry)
        {
            ClearDeepTelemetryUI();
        }

        var activeFan = Enum.TryParse(SettingsManager.Get("LastFanMode", "Auto"), out FanProfile f)
            ? f
            : FanProfile.Auto;
        IsCustomFanEnabled = activeFan == FanProfile.Medium;

        // Initialize Refresh Rate Label
        int maxHz = DisplayManager.GetPrimaryMaxRefreshRate();
        MaxRefreshText = $"{maxHz}Hz";

        // Initialize Settings State
        _chargeLimit = SettingsManager.Get("ChargeLimit", 0) == 1;
        _autoSwitch = SettingsManager.Get("AutoSwitch", 0) == 1;
        _refreshAutoSwitch = SettingsManager.Get("RefreshAutoSwitch", 0) == 1;

        System.Threading.Tasks.Task.Run(() =>
        {
            bool isTurbo = AcerWmiManager.IsTurboModeSupported();
            bool isTaskEnabled = StartupManager.CheckStartupTask();
            bool isNitroKeyEnabled = NitroKeyManager.IsIntegrationEnabled() || SettingsManager.Get("NitroKeyIntegrated", 0) == 1;

            if (isNitroKeyEnabled)
            {
                NitroKeyManager.SyncExecutablePath(Environment.ProcessPath ?? "");
            }

            int core = 0, memory = 0;
            bool hasClocks = _gpuManager.IsValid && _gpuManager.GetClocks(out core, out memory);

            // Push results back to the UI thread asynchronously
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
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

        ActivePowerProfile = AcerWmiManager.GetActivePowerMode() ?? (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

        // Setup Commands
        SetPowerCommand = new RelayCommand(async param =>
        {
            if (Enum.TryParse(param?.ToString(), out PowerProfile mode))
            {
                try
                {
                    ActivePowerProfile = mode;

                    _ = AcerWmiManager.SetPowerModeAsync(mode);
                    SettingsManager.Save("LastPowerMode", (int)mode);
                    bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                    SettingsManager.Save(isOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)mode);

                    if (ManageCpuPower)
                    {
                        _ = CpuPowerManager.ApplyProfileLimitsAsync(mode, isOnline);
                    }

                    await _gpuManager.ApplyPowerProfileOcAsync(mode);
                    if (_gpuManager.GetClocks(out int c, out int m))
                    {
                        GpuCoreOffset = c;
                        GpuMemoryOffset = m;
                    }

                    if (IsCustomFanEnabled && IsCurveModeEnabled)
                    {
                        ApplyCurrentFanCurve();
                    }

                    TrayApplication.Instance?.TriggerProfileOsd(mode);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set power mode: {ex.Message}");
                }
            }
        });

        SetFanCommand = new RelayCommand(param =>
        {
            if (Enum.TryParse(param?.ToString(), out FanProfile mode))
            {
                IsCustomFanEnabled = mode == FanProfile.Medium;
                if (mode == FanProfile.Medium)
                {
                    if (IsCurveModeEnabled)
                        ApplyCurrentFanCurve();
                    else
                        _ = AcerWmiManager.SetCustomFansAsync(CpuFanSpeed, GpuFanSpeed);
                }
                else
                {
                    _ = AcerWmiManager.SetFansAsync(mode);
                }

                SettingsManager.Save("LastFanMode", mode.ToString());
                bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                SettingsManager.Save(isOnline ? "LastAcFanMode" : "LastDcFanMode", mode.ToString());
            }
        });

        SetRefreshCommand = new RelayCommand(param =>
        {
            if (Enum.TryParse(param?.ToString(), out RefreshProfile profile))
            {
                SettingsManager.Save("RefreshMode", (int)profile);
                bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                DisplayManager.ApplyRefreshProfile(profile, isOnline);
            }
        });

        ApplyGpuClocksCommand = new RelayCommand(async _ =>
        {
            try
            {
                var currentProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

                // Save to the active profile
                _gpuManager.SaveCustomProfileOc(currentProfile, GpuCoreOffset, GpuMemoryOffset);
                int result = _gpuManager.SetClocks(GpuCoreOffset, GpuMemoryOffset);

                if (result == 1)
                {
                    ApplyBtnText = "APPLIED!";
                    ApplyBtnColor = "#34C759";
                }
                else
                {
                    ApplyBtnText = "ERROR";
                    ApplyBtnColor = "#FF453A";
                }

                await Task.Delay(2000);
                ApplyBtnText = "APPLY";
                ApplyBtnColor = "#B388FF";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply GPU clocks: {ex.Message}");
            }
        });

        ResetGpuClocksCommand = new RelayCommand(async _ =>
        {
            try
            {
                var currentProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

                // Wipe custom save and load default config
                await _gpuManager.ResetProfileToDefaultsAsync(currentProfile);

                if (_gpuManager.GetClocks(out int c, out int m))
                {
                    GpuCoreOffset = c;
                    GpuMemoryOffset = m;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to reset GPU clocks: {ex.Message}");
            }
        });

        StartTelemetryPolling();
    }

    public string MaxRefreshText { get; }

    // --- FAN PROPERTIES & LOGIC ---
    private int _cpuFanSpeed;

    public int CpuFanSpeed
    {
        get => _cpuFanSpeed;
        set
        {
            if (SetProperty(ref _cpuFanSpeed, value))
            {
                if (IsUnifiedFans) GpuFanSpeed = value;
                TriggerFanSave();
            }
        }
    }

    private int _gpuFanSpeed;

    public int GpuFanSpeed
    {
        get => _gpuFanSpeed;
        set
        {
            if (SetProperty(ref _gpuFanSpeed, value))
            {
                if (IsUnifiedFans) CpuFanSpeed = value;
                TriggerFanSave();
            }
        }
    }

    private bool _isUnifiedFans;

    public bool IsUnifiedFans
    {
        get => _isUnifiedFans;
        set
        {
            if (SetProperty(ref _isUnifiedFans, value))
            {
                SettingsManager.Save("UnifiedFans", value ? 1 : 0);
                if (value) GpuFanSpeed = CpuFanSpeed;
            }
        }
    }

    private bool _isCustomFanEnabled;

    public bool IsCustomFanEnabled
    {
        get => _isCustomFanEnabled;
        set
        {
            if (SetProperty(ref _isCustomFanEnabled, value))
            {
                OnPropertyChanged(nameof(CustomFanOpacity));
                OnPropertyChanged(nameof(IsManualSliderEnabled));
                OnPropertyChanged(nameof(CustomFanHeaderText));
                OnPropertyChanged(nameof(CustomFanHeaderColor));
                OnPropertyChanged(nameof(SliderDisabledTooltip));
            }
        }
    }

    public double CustomFanOpacity => IsCustomFanEnabled ? 1.0 : 0.4;

    private void TriggerFanSave()
    {
        if (!IsCustomFanEnabled || IsCurveModeEnabled) return;
        _fanDebouncer.Debounce(250, () =>
        {
            SettingsManager.Save("CustomFanSpeedCpu", CpuFanSpeed);
            SettingsManager.Save("CustomFanSpeedGpu", GpuFanSpeed);
            _ = AcerWmiManager.SetCustomFansAsync(CpuFanSpeed, GpuFanSpeed);
        });
    }

    public void ApplyCurrentFanCurve()
    {
        Task.Run(async () =>
        {
            try
            {
                string pName = ActivePowerProfile.ToString();
                var cpuCurve = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{pName}", FanCurveHelper.GetDefaultCpuCurve(ActivePowerProfile));
                var gpuCurve = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{pName}", FanCurveHelper.GetDefaultGpuCurve(ActivePowerProfile));

                var telemetry = AcerWmiManager.GetSystemTelemetry();
                int currentCpuTemp = telemetry.CpuTemp > 0 ? telemetry.CpuTemp : 50;
                int currentGpuTemp = telemetry.GpuTemp > 0 ? telemetry.GpuTemp : currentCpuTemp;

                int cpuSpd = Math.Clamp(FanCurveHelper.InterpolateSpeed(cpuCurve, currentCpuTemp), 0, 100);
                int gpuSpd = Math.Clamp(FanCurveHelper.InterpolateSpeed(gpuCurve, currentGpuTemp), 0, 100);

                await AcerWmiManager.SetCustomFansAsync(cpuSpd, gpuSpd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply current fan curve: {ex.Message}");
            }
        });
    }

    // --- SETTINGS PROPERTIES & LOGIC ---
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

    private bool _deepGpuTelemetry;

    public bool DeepGpuTelemetry
    {
        get => _deepGpuTelemetry;
        set
        {
            if (SetProperty(ref _deepGpuTelemetry, value))
            {
                SettingsManager.Save("DeepGpuTelemetry", value ? 1 : 0);
                if (!value) ClearDeepTelemetryUI();
            }
        }
    }

    private void ClearDeepTelemetryUI()
    {
        GpuNameText = "NVIDIA GPU (SLEEPING)";
        GpuArchText = "";
        GpuLoadText = "-- %";
        GpuLoadColor = "#888890";
        GpuVramText = "-- / -- MB";
        GpuDeepTempText = "-- C";
        GpuDeepTempColor = "#888890";
        GpuPStateText = "--";
        GpuCoreClockText = "-- MHz";
        GpuMemClockText = "-- MHz";
        GpuPowerText = "-- W";
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

    // --- COMMANDS ---
    public ICommand SetPowerCommand { get; }
    public ICommand SetFanCommand { get; }
    public ICommand SetRefreshCommand { get; }
    public ICommand ApplyGpuClocksCommand { get; }
    public ICommand ResetGpuClocksCommand { get; }

    private void StartTelemetryPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        // Offload the loop entirely to a background ThreadPool thread
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            int sleepSkipTicks = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Run Acer WMI and optionally Nvidia SMI concurrently in the background
                    var wmiTask = Task.Run(() => AcerWmiManager.GetSystemTelemetry(), token);

                    // Conditionally fetch deep NVIDIA SMI stats with sleep back-off to protect battery
                    Task<NvidiaGpuManager.GpuTelemetry>? smiTask = null;
                    if (DeepGpuTelemetry)
                    {
                        if (sleepSkipTicks > 0)
                        {
                            sleepSkipTicks--;
                        }
                        else
                        {
                            smiTask = _gpuManager.GetNvmlTelemetryAsync(token);
                        }
                    }

                    if (smiTask != null)
                    {
                        await Task.WhenAll(wmiTask, smiTask);
                    }
                    else
                    {
                        await wmiTask;
                    }

                    var telemetry = await wmiTask;
                    var smi = smiTask != null ? await smiTask : null;

                    if (DeepGpuTelemetry && smiTask != null)
                    {
                        // If dGPU is asleep (CoreTemp 0 or unknown name), back off polling for 3 intervals (6 seconds)
                        // to prevent repeatedly waking up the discrete GPU into full power state
                        if (smi == null || string.IsNullOrEmpty(smi.Name) || smi.Name == "Unknown" || smi.CoreTemp == 0)
                        {
                            sleepSkipTicks = 3;
                        }
                        else
                        {
                            sleepSkipTicks = 0;
                        }
                    }

                    // Push property changes asynchronously to the WPF UI Thread (non-blocking)
                    _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        // 1. Update WMI CPU/GPU Telemetry
                        CpuTempText = telemetry.CpuTemp > 0 ? $"{telemetry.CpuTemp} C" : "-- C";
                        CpuRpmText = telemetry.CpuRpm > 0 ? $"{telemetry.CpuRpm} RPM" : "-- RPM";
                        CpuTempColor = telemetry.CpuTemp > 85 ? "#FF453A" : "White";

                        GpuTempText = telemetry.GpuTemp > 0 ? $"{telemetry.GpuTemp} C" : "-- C";
                        GpuRpmText = telemetry.GpuRpm > 0 ? $"{telemetry.GpuRpm} RPM" : "-- RPM";
                        GpuTempColor = telemetry.GpuTemp > 85 ? "#FF453A" : "White";

                        // 2. Update NVIDIA SMI Deep Telemetry
                        if (smi != null && !string.IsNullOrEmpty(smi.Name) && smi.Name != "Unknown")
                        {
                            GpuNameText = smi.Name;
                            GpuArchText = smi.Architecture;

                            GpuLoadText = $"{smi.GpuLoad}%";
                            GpuLoadColor = smi.GpuLoad >= 95 ? "#FF453A" : (smi.GpuLoad >= 80 ? "#FF9F0A" : "White");

                            GpuVramText = $"{smi.VramUsedMb} / {smi.VramTotalMb} MB";

                            GpuDeepTempText = $"{smi.CoreTemp} C";
                            GpuDeepTempColor =
                                smi.CoreTemp >= 85 ? "#FF453A" : (smi.CoreTemp >= 78 ? "#FF9F0A" : "White");

                            GpuPStateText = smi.PState;
                            GpuCoreClockText = $"{smi.CurrentCoreClock} MHz";
                            GpuMemClockText = $"{smi.CurrentMemoryClock} MHz";

                            if (smi.EnforcedPowerLimitW > 0 && smi.MaxPowerLimitW > 0)
                            {
                                GpuPowerText =
                                    $"{smi.PowerDrawW:0.0} / {smi.EnforcedPowerLimitW:0} / {smi.MaxPowerLimitW:0} W";
                            }
                            else if (smi.EnforcedPowerLimitW > 0)
                            {
                                GpuPowerText = $"{smi.PowerDrawW:0.0} / {smi.EnforcedPowerLimitW:0} W";
                            }
                            else
                            {
                                GpuPowerText = $"{smi.PowerDrawW:0.0} W";
                            }
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);

                    // Wait asynchronously for the next 2-second interval tick
                    await timer.WaitForNextTickAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break; // Gracefully exit when token is cancelled
                }
                catch
                {
                    // Silently absorb loop exceptions to prevent background thread crash
                }
            }
        }, token);
    }

    public void PausePolling()
    {
        _pollingCts?.Cancel();
    }

    public void ResumePolling()
    {
        StartTelemetryPolling();
    }

    public void Dispose()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _fanDebouncer.Dispose();
        _gpuManager.Dispose();
    }
}
