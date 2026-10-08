using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Mvvm;
using System.Windows;
using System.Windows.Media;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using SystemInformation = System.Windows.Forms.SystemInformation;

namespace Nitrous.Ui;

public class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ActionDebouncer _fanDebouncer = new ActionDebouncer();
    private readonly NvidiaGpuManager _gpuManager = new();
    private CancellationTokenSource? _pollingCts;
    private PerformanceCounter? _cpuUsageCounter;

    public struct TelemetryPoint
    {
        public long Timestamp;
        public double Temp;
        public double Usage;
    }

    private List<TelemetryPoint> _cpuHistory = new();
    private List<TelemetryPoint> _gpuHistory = new();

    private PointCollection _cpuTempPoints = new PointCollection();

    public PointCollection CpuTempPoints
    {
        get => _cpuTempPoints;
        set => SetProperty(ref _cpuTempPoints, value);
    }

    private PointCollection _cpuTempFillPoints = new PointCollection();

    public PointCollection CpuTempFillPoints
    {
        get => _cpuTempFillPoints;
        set => SetProperty(ref _cpuTempFillPoints, value);
    }

    private PointCollection _cpuUsagePoints = new PointCollection();

    public PointCollection CpuUsagePoints
    {
        get => _cpuUsagePoints;
        set => SetProperty(ref _cpuUsagePoints, value);
    }

    private PointCollection _cpuUsageFillPoints = new PointCollection();

    public PointCollection CpuUsageFillPoints
    {
        get => _cpuUsageFillPoints;
        set => SetProperty(ref _cpuUsageFillPoints, value);
    }

    private PointCollection _gpuTempPoints = new PointCollection();

    public PointCollection GpuTempPoints
    {
        get => _gpuTempPoints;
        set => SetProperty(ref _gpuTempPoints, value);
    }

    private PointCollection _gpuTempFillPoints = new PointCollection();

    public PointCollection GpuTempFillPoints
    {
        get => _gpuTempFillPoints;
        set => SetProperty(ref _gpuTempFillPoints, value);
    }

    private PointCollection _gpuUsagePoints = new PointCollection();

    public PointCollection GpuUsagePoints
    {
        get => _gpuUsagePoints;
        set => SetProperty(ref _gpuUsagePoints, value);
    }

    private PointCollection _gpuUsageFillPoints = new PointCollection();

    public PointCollection GpuUsageFillPoints
    {
        get => _gpuUsageFillPoints;
        set => SetProperty(ref _gpuUsageFillPoints, value);
    }

    private double _gpuGraphOpacity = 1.0;

    public double GpuGraphOpacity
    {
        get => _gpuGraphOpacity;
        set => SetProperty(ref _gpuGraphOpacity, value);
    }

    public IReadOnlyList<TelemetryPoint> CpuHistory => _cpuHistory;
    public IReadOnlyList<TelemetryPoint> GpuHistory => _gpuHistory;

    private string _cpuTempText = "--°C";

    public string CpuTempText
    {
        get => _cpuTempText;
        set => SetProperty(ref _cpuTempText, value);
    }

    private string _cpuUsageText = "--%";

    public string CpuUsageText
    {
        get => _cpuUsageText;
        set => SetProperty(ref _cpuUsageText, value);
    }

    private string _cpuMaxStatsText = "";

    public string CpuMaxStatsText
    {
        get => _cpuMaxStatsText;
        set => SetProperty(ref _cpuMaxStatsText, value);
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

    private string _gpuUsageText = "--%";

    public string GpuUsageText
    {
        get => _gpuUsageText;
        set => SetProperty(ref _gpuUsageText, value);
    }

    private string _gpuMaxStatsText = "";

    public string GpuMaxStatsText
    {
        get => _gpuMaxStatsText;
        set => SetProperty(ref _gpuMaxStatsText, value);
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

    private string _batteryBadgeText = "AC POWER";

    public string BatteryBadgeText
    {
        get => _batteryBadgeText;
        set => SetProperty(ref _batteryBadgeText, value);
    }

    private string _batteryBadgeBorder = "#FF4500";

    public string BatteryBadgeBorder
    {
        get => _batteryBadgeBorder;
        set => SetProperty(ref _batteryBadgeBorder, value);
    }

    private string _batteryBadgeForeground = "White";

    public string BatteryBadgeForeground
    {
        get => _batteryBadgeForeground;
        set => SetProperty(ref _batteryBadgeForeground, value);
    }

    private string _batteryBadgeIcon =
        "M16,7V3H14V7H10V3H8V7C8,10 9.79,11.4 11,11.83V16H13V11.83C14.21,11.4 16,10 16,7M10,18H14V22H10V18Z";

    public string BatteryBadgeIcon
    {
        get => _batteryBadgeIcon;
        set => SetProperty(ref _batteryBadgeIcon, value);
    }

    private string _batteryHealthText = "Evaluating...";

    public string BatteryHealthText
    {
        get => _batteryHealthText;
        set => SetProperty(ref _batteryHealthText, value);
    }

    private string _batteryHealthPercentText = "--%";

    public string BatteryHealthPercentText
    {
        get => _batteryHealthPercentText;
        set => SetProperty(ref _batteryHealthPercentText, value);
    }

    private string _batteryCycleText = "-- cycles";

    public string BatteryCycleText
    {
        get => _batteryCycleText;
        set => SetProperty(ref _batteryCycleText, value);
    }

    private string _applyBtnText = "APPLY";

    public string ApplyBtnText
    {
        get => _applyBtnText;
        set => SetProperty(ref _applyBtnText, value);
    }

    private string _applyBtnColor = "#FF4500";

    public string ApplyBtnColor
    {
        get => _applyBtnColor;
        set => SetProperty(ref _applyBtnColor, value);
    }

    private string _acerServicesStatusText = "Scanning...";

    public string AcerServicesStatusText
    {
        get => _acerServicesStatusText;
        set => SetProperty(ref _acerServicesStatusText, value);
    }

    private string _acerServicesActionText = "STOP & DISABLE";

    public string AcerServicesActionText
    {
        get => _acerServicesActionText;
        set => SetProperty(ref _acerServicesActionText, value);
    }

    public ObservableCollection<AcerServiceInfo> AcerServicesList { get; } = new();

    private System.Windows.Visibility _acerServicesListVisibility = System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility AcerServicesListVisibility
    {
        get => _acerServicesListVisibility;
        set => SetProperty(ref _acerServicesListVisibility, value);
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
                bool isOnline = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                                System.Windows.Forms.PowerLineStatus.Online;

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

    private string _gpuLoadColor = "#FF4500";

    public string GpuLoadColor
    {
        get => _gpuLoadColor;
        set => SetProperty(ref _gpuLoadColor, value);
    }

    private string _gpuDeepTempColor = "#FF4500";

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
    public string CustomFanHeaderColor => IsCurveModeEnabled ? "#FFFFFF" : "#FFFFFF";
    public string FanModeSubtext => IsCurveModeEnabled ? "CURVE ACTIVE" : "MANUAL (FIXED)";
    public string FanModeSubtextColor => IsCurveModeEnabled ? "#FFFFFF" : "#FF4500";

    public string SliderDisabledTooltip => IsCurveModeEnabled
        ? "Manual sliders are disabled while Fan Curve is active. Adjust your curve in CURVE EDITOR."
        : "Adjust fixed fan percentage";

    // --- CPU HOVER ---
    private Visibility _cpuHoverVisibility = Visibility.Collapsed;

    public Visibility CpuHoverVisibility
    {
        get => _cpuHoverVisibility;
        set => SetProperty(ref _cpuHoverVisibility, value);
    }

    private Thickness _cpuHoverMargin;

    public Thickness CpuHoverMargin
    {
        get => _cpuHoverMargin;
        set => SetProperty(ref _cpuHoverMargin, value);
    }

    private Thickness _cpuTooltipMargin;

    public Thickness CpuTooltipMargin
    {
        get => _cpuTooltipMargin;
        set => SetProperty(ref _cpuTooltipMargin, value);
    }

    private string _cpuHoverTime = "";

    public string CpuHoverTime
    {
        get => _cpuHoverTime;
        set => SetProperty(ref _cpuHoverTime, value);
    }

    private string _cpuHoverTempStr = "--";

    public string CpuHoverTempStr
    {
        get => _cpuHoverTempStr;
        set => SetProperty(ref _cpuHoverTempStr, value);
    }

    private string _cpuHoverUsageStr = "--";

    public string CpuHoverUsageStr
    {
        get => _cpuHoverUsageStr;
        set => SetProperty(ref _cpuHoverUsageStr, value);
    }

    // --- GPU HOVER ---
    private Visibility _gpuHoverVisibility = Visibility.Collapsed;

    public Visibility GpuHoverVisibility
    {
        get => _gpuHoverVisibility;
        set => SetProperty(ref _gpuHoverVisibility, value);
    }

    private Visibility _gpuHoverStatsVisibility = Visibility.Visible;

    public Visibility GpuHoverStatsVisibility
    {
        get => _gpuHoverStatsVisibility;
        set => SetProperty(ref _gpuHoverStatsVisibility, value);
    }

    private Visibility _gpuHoverSleepVisibility = Visibility.Collapsed;

    public Visibility GpuHoverSleepVisibility
    {
        get => _gpuHoverSleepVisibility;
        set => SetProperty(ref _gpuHoverSleepVisibility, value);
    }

    private Thickness _gpuHoverMargin;

    public Thickness GpuHoverMargin
    {
        get => _gpuHoverMargin;
        set => SetProperty(ref _gpuHoverMargin, value);
    }

    private Thickness _gpuTooltipMargin;

    public Thickness GpuTooltipMargin
    {
        get => _gpuTooltipMargin;
        set => SetProperty(ref _gpuTooltipMargin, value);
    }

    private string _gpuHoverTime = "";

    public string GpuHoverTime
    {
        get => _gpuHoverTime;
        set => SetProperty(ref _gpuHoverTime, value);
    }

    private string _gpuHoverTempStr = "--";

    public string GpuHoverTempStr
    {
        get => _gpuHoverTempStr;
        set => SetProperty(ref _gpuHoverTempStr, value);
    }

    private string _gpuHoverUsageStr = "--";

    public string GpuHoverUsageStr
    {
        get => _gpuHoverUsageStr;
        set => SetProperty(ref _gpuHoverUsageStr, value);
    }

    public void UpdateCpuHover(double pixelX, double actualWidth)
    {
        long now = Environment.TickCount64;
        long targetTime = now - 300000 + (long)((pixelX / actualWidth) * 300000);
        var point = _cpuHistory.OrderBy(p => Math.Abs(p.Timestamp - targetTime)).FirstOrDefault();

        if (point.Timestamp > 0 && Math.Abs(point.Timestamp - targetTime) < 10000)
        {
            CpuHoverVisibility = Visibility.Visible;
            CpuHoverMargin = new Thickness(pixelX, 0, 0, 0);
            CpuTooltipMargin = new Thickness(pixelX > (actualWidth / 2) ? pixelX - 85 : pixelX + 5, 5, 0, 0);
            TimeSpan diff = TimeSpan.FromMilliseconds(now - point.Timestamp);
            CpuHoverTime = $"-{(int)diff.TotalMinutes}m {(int)diff.Seconds}s";
            CpuHoverTempStr = $"{point.Temp}°C";
            CpuHoverUsageStr = $"{point.Usage:0}%";
        }
        else
        {
            CpuHoverVisibility = Visibility.Collapsed;
        }
    }

    public void UpdateGpuHover(double pixelX, double actualWidth)
    {
        long now = Environment.TickCount64;
        long targetTime = now - 300000 + (long)((pixelX / actualWidth) * 300000);
        var point = _gpuHistory.OrderBy(p => Math.Abs(p.Timestamp - targetTime)).FirstOrDefault();

        if (point.Timestamp > 0 && Math.Abs(point.Timestamp - targetTime) < 10000)
        {
            GpuHoverVisibility = Visibility.Visible;
            GpuHoverMargin = new Thickness(pixelX, 0, 0, 0);
            GpuTooltipMargin = new Thickness(pixelX > (actualWidth / 2) ? pixelX - 85 : pixelX + 5, 5, 0, 0);
            TimeSpan diff = TimeSpan.FromMilliseconds(now - point.Timestamp);
            GpuHoverTime = $"-{(int)diff.TotalMinutes}m {(int)diff.Seconds}s";
            GpuHoverTempStr = $"{point.Temp}°C";
            GpuHoverUsageStr = $"{point.Usage:0}%";
            if (point.Temp == 0)
            {
                GpuHoverStatsVisibility = Visibility.Collapsed;
                GpuHoverSleepVisibility = Visibility.Visible;
            }
            else
            {
                GpuHoverStatsVisibility = Visibility.Visible;
                GpuHoverSleepVisibility = Visibility.Collapsed;
            }
        }
        else
        {
            GpuHoverVisibility = Visibility.Collapsed;
        }
    }

    public DashboardViewModel()
    {
        try
        {
            // Use 'Processor Information' and '% Processor Utility' to match Task Manager
            _cpuUsageCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
        }
        catch
        {
        }

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

        if (!IsTestHost())
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                bool isTurbo = AcerWmiManager.IsTurboModeSupported();
                bool isTaskEnabled = StartupManager.CheckStartupTask();
                bool isNitroKeyEnabled = NitroKeyManager.IsIntegrationEnabled() ||
                                         SettingsManager.Get("NitroKeyIntegrated", 0) == 1;

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
        }

        ActivePowerProfile = AcerWmiManager.GetActivePowerMode() ??
                             (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

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
                ApplyBtnColor = "#FF4500";
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

        ToggleAcerServicesCommand = new RelayCommand(async _ =>
        {
            AcerServicesActionText = "PLEASE WAIT...";
            var summary = AcerServiceManager.GetServiceSummary();
            if (summary.RunningCount > 0)
            {
                await AcerServiceManager.StopAndDisableAllAsync();
            }
            else
            {
                await AcerServiceManager.RestoreAndStartAllAsync();
            }
            _ = RefreshAcerServicesAsync();
        });

        KillAcerServiceCommand = new RelayCommand(async param =>
        {
            if (param is string serviceName)
            {
                await AcerServiceManager.StopAndDisableSingleAsync(serviceName);
                _ = RefreshAcerServicesAsync();
            }
        });

        _ = RefreshBatteryHealthAsync();
        _ = RefreshAcerServicesAsync();
        StartTelemetryPolling();
    }

    public async Task RefreshAcerServicesAsync()
    {
        var summary = await Task.Run(() => AcerServiceManager.GetServiceSummary());
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (summary.TotalFound == 0)
            {
                AcerServicesStatusText = "No Acer services found.";
                AcerServicesActionText = "N/A";
                AcerServicesList.Clear();
                AcerServicesListVisibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                if (summary.RunningCount > 0)
                {
                    AcerServicesStatusText = $"{summary.RunningCount} OEM services running";
                    AcerServicesActionText = "STOP & DISABLE";
                }
                else
                {
                    AcerServicesStatusText = "All Acer services stopped & disabled";
                    AcerServicesActionText = "RESTORE";
                }

                var runningServices = summary.Services.Where(s => s.Status == System.ServiceProcess.ServiceControllerStatus.Running || s.Status == System.ServiceProcess.ServiceControllerStatus.StartPending).ToList();
                if (runningServices.Any())
                {
                    AcerServicesList.Clear();
                    foreach (var s in runningServices)
                    {
                        AcerServicesList.Add(s);
                    }
                    AcerServicesListVisibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    AcerServicesList.Clear();
                    AcerServicesListVisibility = System.Windows.Visibility.Collapsed;
                }
            }
        });
    }

    private async Task RefreshBatteryHealthAsync()
    {
        var health = await Task.Run(() => BatteryManager.GetBatteryHealth());
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (health != null && health.Value.DesignedCapacity > 0)
            {
                var val = health.Value;
                BatteryHealthText = $"{val.FullChargedCapacity:N0} / {val.DesignedCapacity:N0} mWh";
                BatteryHealthPercentText = $"{val.HealthPercent:0.0}%";
                BatteryCycleText = $"{val.CycleCount} cycles";
            }
            else
            {
                BatteryHealthText = "ACPI Data Missing";
                BatteryHealthPercentText = "Unknown Health";
                BatteryCycleText = "Unknown Cycles";
            }
        });
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
                string pName = SettingsManager.Get("ActiveFanCurvePreset", "Auto");
                if (pName == "Auto") pName = ActivePowerProfile.ToString();

                var cpuCurve = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{pName}",
                    FanCurveHelper.GetDefaultCpuCurve(ActivePowerProfile));
                var gpuCurve = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{pName}",
                    FanCurveHelper.GetDefaultGpuCurve(ActivePowerProfile));

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

    public void ClearDeepTelemetryUI()
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

    private System.Windows.Visibility _gpuSleepOverlayVisibility = System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility GpuSleepOverlayVisibility
    {
        get => _gpuSleepOverlayVisibility;
        set => SetProperty(ref _gpuSleepOverlayVisibility, value);
    }

    // --- COMMANDS ---
    public ICommand SetPowerCommand { get; }
    public ICommand SetFanCommand { get; }
    public ICommand SetRefreshCommand { get; }
    public ICommand ApplyGpuClocksCommand { get; }
    public ICommand ResetGpuClocksCommand { get; }
    public ICommand ToggleAcerServicesCommand { get; }
    public ICommand KillAcerServiceCommand { get; }

    private void StartTelemetryPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        // Offload the loop entirely to a background ThreadPool thread
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.5));
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Run Battery Telemetry
                    var batteryState = BatteryManager.GetBatteryState();

                    // Run Acer WMI telemetry first for CPU stats and fan RPMs
                    var telemetry = await Task.Run(() => AcerWmiManager.GetSystemTelemetry(), token);

                    // Use the Windows CM API (DEVPKEY_Device_PowerData) to determine the actual
                    // GPU device power state (D0 = awake, D3 = asleep). This is the same technique
                    // used by the NVIDIA GPU Activity tray icon and OpenSense.
                    // Unlike the Acer EC temperature (which returns stale/frozen values), the CM API
                    // reads from Windows' own records and never touches the GPU driver.
                    NvidiaGpuManager.GpuTelemetry? smi = null;
                    bool gpuIsAwake = NvidiaGpuManager.IsGpuAwake();

                    if (gpuIsAwake)
                    {
                        smi = await _gpuManager.GetNvmlTelemetryAsync(token);
                    }

                    // Push property changes asynchronously to the WPF UI Thread (non-blocking)
                    _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        // 0. Update Battery Telemetry
                        string acIcon =
                            "M7,2V13H10V22L17,10H13L17,2H7Z";
                        string battIcon =
                            "M16.67,4H15V2H9V4H7.33A1.33,1.33 0 0,0 6,5.33V20.67C6,21.4 6.6,22 7.33,22H16.67A1.33,1.33 0 0,0 18,20.67V5.33C18,4.6 17.4,4 16.67,4Z";

                        double watts = Math.Abs(batteryState.Rate) / 1000.0;

                        if (batteryState.AcOnLine)
                        {
                            if (batteryState.Charging && watts > 0.5)
                            {
                                BatteryBadgeText = $"CHARGING • {watts:0.0}W";
                                BatteryBadgeBorder = "#FF4500";
                                BatteryBadgeForeground = "#FF4500"; // Orange accent
                                BatteryBadgeIcon = acIcon;
                            }
                            else
                            {
                                BatteryBadgeText = "AC POWER";
                                BatteryBadgeBorder = "#222226"; // Subdued border
                                BatteryBadgeForeground = "White";
                                BatteryBadgeIcon = acIcon;
                            }
                        }
                        else
                        {
                            BatteryBadgeText = $"BATTERY • {watts:0.0}W";
                            BatteryBadgeBorder = "#222226"; // Subdued border
                            BatteryBadgeForeground = "White";
                            BatteryBadgeIcon = battIcon;
                        }

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
                            // Override WMI GPU temp with highly-accurate NVML temp if awake
                            if (smi.CoreTemp > 0)
                            {
                                GpuTempText = $"{smi.CoreTemp} C";
                                GpuTempColor = smi.CoreTemp >= 85 ? "#FF453A" : "White";
                            }

                            if (DeepGpuTelemetry)
                            {
                                GpuNameText = smi.Name;
                                GpuArchText = smi.Architecture;

                                GpuLoadText = $"{smi.GpuLoad}%";
                                GpuLoadColor = smi.GpuLoad >= 95
                                    ? "#FF453A"
                                    : (smi.GpuLoad >= 80 ? "#FF9F0A" : "White");

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
                        }

                        // 3. Update Graphing Data
                        float cpuUsage = 0;
                        if (_cpuUsageCounter != null)
                        {
                            try
                            {
                                cpuUsage = _cpuUsageCounter.NextValue();
                            }
                            catch
                            {
                            }
                        }

                        CpuUsageText = $"{cpuUsage:0}%";

                        long now = Environment.TickCount64;
                        _cpuHistory.Add(new TelemetryPoint
                        {
                            Timestamp = now, Temp = telemetry.CpuTemp > 0 ? telemetry.CpuTemp : 0, Usage = cpuUsage
                        });

                        bool isSmiValid = smi != null && smi.PState != "Sleep";

                        if (isSmiValid)
                        {
                            GpuGraphOpacity = 1.0;
                            GpuUsageText = $"{smi!.GpuLoad}%";
                            int gpuTemp = smi.CoreTemp > 0 ? smi.CoreTemp : telemetry.GpuTemp;
                            _gpuHistory.Add(new TelemetryPoint
                                { Timestamp = now, Temp = gpuTemp, Usage = smi.GpuLoad });
                        }
                        else if (gpuIsAwake)
                        {
                            // GPU is awake (D0) but NVML failed or is unavailable
                            GpuGraphOpacity = 1.0;
                            GpuUsageText = "--%";
                            _gpuHistory.Add(new TelemetryPoint
                                { Timestamp = now, Temp = telemetry.GpuTemp, Usage = 0 });
                        }
                        else
                        {
                            // GPU is in D3/D3Cold sleep (confirmed by CM API)
                            GpuGraphOpacity = 0.3;
                            GpuUsageText = "Sleep";
                            _gpuHistory.Add(new TelemetryPoint { Timestamp = now, Temp = 0, Usage = 0 });
                        }

                        GpuSleepOverlayVisibility = gpuIsAwake
                            ? System.Windows.Visibility.Collapsed
                            : System.Windows.Visibility.Visible;

                        // Prune > 5 mins (300,000 ms)
                        _cpuHistory.RemoveAll(p => now - p.Timestamp > 300000);
                        _gpuHistory.RemoveAll(p => now - p.Timestamp > 300000);

                        var maxCpuTemp = _cpuHistory.Count > 0 ? _cpuHistory.Max(p => p.Temp) : 0;
                        var maxCpuUsage = _cpuHistory.Count > 0 ? _cpuHistory.Max(p => p.Usage) : 0;
                        CpuMaxStatsText = $"Max: {maxCpuTemp}°C  {maxCpuUsage:0}%";

                        var maxGpuTemp = _gpuHistory.Count > 0 ? _gpuHistory.Max(p => p.Temp) : 0;
                        var maxGpuUsage = _gpuHistory.Count > 0 ? _gpuHistory.Max(p => p.Usage) : 0;
                        GpuMaxStatsText = $"Max: {maxGpuTemp}°C  {maxGpuUsage:0}%";

                        CpuTempPoints = GenerateGraphPoints(_cpuHistory, true, now);
                        CpuTempFillPoints = GenerateGraphFillPoints(_cpuHistory, true, now);
                        CpuUsagePoints = GenerateGraphPoints(_cpuHistory, false, now);
                        CpuUsageFillPoints = GenerateGraphFillPoints(_cpuHistory, false, now);
                        GpuTempPoints = GenerateGraphPoints(_gpuHistory, true, now);
                        GpuTempFillPoints = GenerateGraphFillPoints(_gpuHistory, true, now);
                        GpuUsagePoints = GenerateGraphPoints(_gpuHistory, false, now);
                        GpuUsageFillPoints = GenerateGraphFillPoints(_gpuHistory, false, now);
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

    public bool IsPollingActive => _pollingCts != null && !_pollingCts.IsCancellationRequested;

    public void PausePolling()
    {
        _pollingCts?.Cancel();
    }

    public void ResumePolling()
    {
        StartTelemetryPolling();
    }

    private PointCollection GenerateGraphPoints(List<TelemetryPoint> history, bool isTemp, long now)
    {
        var points = new PointCollection();
        if (history.Count == 0) return points;

        long windowStart = now - 300000;
        double graphWidth = 140.5;
        double graphHeight = 74.0;
        
        for (int i = 0; i < history.Count; i++)
        {
            var p = history[i];
            double x = ((p.Timestamp - windowStart) / 300000.0) * graphWidth;
            double val = Math.Clamp(isTemp ? p.Temp : p.Usage, 0, 100);
            double y = ((100 - val) / 100.0) * graphHeight;
            points.Add(new System.Windows.Point(x, y));
        }

        return points;
    }

    private PointCollection GenerateGraphFillPoints(List<TelemetryPoint> history, bool isTemp, long now)
    {
        var points = new PointCollection();
        if (history.Count == 0) return points;

        long windowStart = now - 300000;
        double graphWidth = 140.5;
        double graphHeight = 74.0;

        // Bottom left
        double firstX = ((history[0].Timestamp - windowStart) / 300000.0) * graphWidth;
        points.Add(new System.Windows.Point(firstX, graphHeight));

        for (int i = 0; i < history.Count; i++)
        {
            var p = history[i];
            double x = ((p.Timestamp - windowStart) / 300000.0) * graphWidth;
            double val = Math.Clamp(isTemp ? p.Temp : p.Usage, 0, 100);
            double y = ((100 - val) / 100.0) * graphHeight;
            points.Add(new System.Windows.Point(x, y));
        }

        // Bottom right
        double lastX = ((history[^1].Timestamp - windowStart) / 300000.0) * graphWidth;
        points.Add(new System.Windows.Point(lastX, graphHeight));

        return points;
    }

    public void SyncSettings()
    {
        var newPowerProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        if (_activePowerProfile != newPowerProfile)
        {
            _activePowerProfile = newPowerProfile;
            OnPropertyChanged(nameof(ActivePowerProfile));
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
    }

    public void Dispose()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _fanDebouncer.Dispose();
        _gpuManager.Dispose();
    }

    private static bool IsTestHost()
    {
        string procName = Process.GetCurrentProcess().ProcessName;
        return procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
               procName.Contains("vstest", StringComparison.OrdinalIgnoreCase);
    }
}
