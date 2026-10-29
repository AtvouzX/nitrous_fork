using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Mvvm;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using SystemInformation = System.Windows.Forms.SystemInformation;

namespace Nitrous.Ui;

public partial class DashboardViewModel
{
    private bool _manageCpuPower;

    public bool ManageCpuPower
    {
        get => _manageCpuPower;
        set
        {
            if (SetProperty(ref _manageCpuPower, value))
            {
                SettingsManager.Save("ManageCpuPower", value ? 1 : 0);
                bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;

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

    private bool _isTurboSupported;

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
                OnPropertyChanged(nameof(FanReadViewVisibility));
                OnPropertyChanged(nameof(FanWriteViewVisibility));
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
                OnPropertyChanged(nameof(FanReadViewVisibility));
                OnPropertyChanged(nameof(FanWriteViewVisibility));
                OnPropertyChanged(nameof(CustomFanHeaderText));
                OnPropertyChanged(nameof(CustomFanHeaderColor));
                OnPropertyChanged(nameof(SliderDisabledTooltip));
            }
        }
    }

    public double CustomFanOpacity => IsCustomFanEnabled ? 1.0 : 0.4;

    public Visibility FanReadViewVisibility => IsManualSliderEnabled ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FanWriteViewVisibility => IsManualSliderEnabled ? Visibility.Visible : Visibility.Collapsed;

    private int _cpuFanPercentage = 50;

    public int CpuFanPercentage
    {
        get => _cpuFanPercentage;
        set
        {
            if (SetProperty(ref _cpuFanPercentage, value))
            {
                OnPropertyChanged(nameof(CpuFanPctText));
            }
        }
    }

    private int _gpuFanPercentage = 50;

    public int GpuFanPercentage
    {
        get => _gpuFanPercentage;
        set
        {
            if (SetProperty(ref _gpuFanPercentage, value))
            {
                OnPropertyChanged(nameof(GpuFanPctText));
            }
        }
    }

    public string CpuFanPctText => $"{CpuFanPercentage}%";
    public string GpuFanPctText => $"{GpuFanPercentage}%";

    private RefreshProfile _activeRefreshProfile;

    public RefreshProfile ActiveRefreshProfile
    {
        get => _activeRefreshProfile;
        set
        {
            if (SetProperty(ref _activeRefreshProfile, value))
            {
                OnPropertyChanged(nameof(RefreshBadgeText));
            }
        }
    }

    public string RefreshBadgeText => ActiveRefreshProfile switch
    {
        RefreshProfile.Hz60 => "60HZ",
        RefreshProfile.MaxHz => MaxRefreshText,
        _ => "AUTO"
    };

    public string AutoRefreshSubtext => $"60/{MaxRefreshText.Replace("HZ", "").Trim()}";

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
                LogManager.Warn($"Failed to apply current fan curve: {ex.Message}", "DashboardVM");
            }
        });
    }

    public string MaxRefreshText { get; private set; } = "60HZ";

    public ICommand SetPowerCommand { get; private set; } = null!;
    public ICommand SetFanCommand { get; private set; } = null!;
    public ICommand SetRefreshCommand { get; private set; } = null!;

    private void InitializeFansAndProfiles()
    {
        _cpuFanSpeed = SettingsManager.Get("CustomFanSpeedCpu", 50);
        _gpuFanSpeed = SettingsManager.Get("CustomFanSpeedGpu", 50);
        _cpuFanPercentage = _cpuFanSpeed;
        _gpuFanPercentage = _gpuFanSpeed;
        _isUnifiedFans = SettingsManager.Get("UnifiedFans", 1) == 1;
        _isCurveModeEnabled = SettingsManager.Get("IsCurveModeEnabled", 0) == 1;
        _manageCpuPower = SettingsManager.Get("ManageCpuPower", 0) == 1;
        _activeRefreshProfile = (RefreshProfile)SettingsManager.Get("RefreshMode", (int)RefreshProfile.Auto);

        var activeFan = Enum.TryParse(SettingsManager.Get("LastFanMode", "Auto"), out FanProfile f)
            ? f
            : FanProfile.Auto;
        IsCustomFanEnabled = activeFan == FanProfile.Medium;

        int maxHz = DisplayManager.GetPrimaryMaxRefreshRate();
        MaxRefreshText = $"{maxHz}HZ";

        SetPowerCommand = new RelayCommand(async param =>
        {
            if (Enum.TryParse(param?.ToString(), out PowerProfile mode))
            {
                try
                {
                    ActivePowerProfile = mode;
                    DismissGpuAlert();

                    _ = AcerWmiManager.SetPowerModeAsync(mode);
                    SettingsManager.Save("LastPowerMode", (int)mode);
                    bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                    SettingsManager.Save(isOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)mode);

                    if (ManageCpuPower)
                    {
                        _ = CpuPowerManager.ApplyProfileLimitsAsync(mode, isOnline);
                    }

                    await _gpuManager.ApplyPowerProfileOcAsync(mode, isOnline);
                    GpuCoreOffset = _gpuManager.ClampCoreOffset(SettingsManager.Get($"GpuCore_{mode}", _gpuManager.GetDefaultCore(mode)));
                    GpuMemoryOffset = _gpuManager.ClampMemoryOffset(SettingsManager.Get($"GpuMemory_{mode}", _gpuManager.GetDefaultMemory(mode)));

                    if (IsCustomFanEnabled && IsCurveModeEnabled)
                    {
                        ApplyCurrentFanCurve();
                    }

                    TrayApplication.Instance?.TriggerProfileOsd(mode);
                }
                catch (Exception ex)
                {
                    LogManager.Warn($"Failed to set power mode: {ex.Message}", "DashboardVM");
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
                    {
                        ApplyCurrentFanCurve();
                    }
                    else
                    {
                        CpuFanPercentage = CpuFanSpeed;
                        GpuFanPercentage = GpuFanSpeed;
                        _ = AcerWmiManager.SetCustomFansAsync(CpuFanSpeed, GpuFanSpeed);
                    }
                }
                else if (mode == FanProfile.Max)
                {
                    CpuFanPercentage = 100;
                    GpuFanPercentage = 100;
                    _ = AcerWmiManager.SetFansAsync(mode);
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
            RefreshProfile profile;
            if (param is RefreshProfile rp)
            {
                profile = rp;
            }
            else if (!Enum.TryParse(param?.ToString(), out profile))
            {
                return;
            }

            ActiveRefreshProfile = profile;
            SettingsManager.Save("RefreshMode", (int)profile);
            bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
            DisplayManager.ApplyRefreshProfile(profile, isOnline);
        });
    }
}
