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

    private bool _isEcoSupported;

    public bool IsEcoSupported
    {
        get => _isEcoSupported;
        set => SetProperty(ref _isEcoSupported, value);
    }

    private PowerProfile _activePowerProfile;

    public PowerProfile ActivePowerProfile
    {
        get => _activePowerProfile;
        set
        {
            if (SetProperty(ref _activePowerProfile, value))
            {
                OnPropertyChanged(nameof(IsFanControlLocked));
                OnPropertyChanged(nameof(IsFanControlEnabled));
                OnPropertyChanged(nameof(FanLockVisibility));
                OnPropertyChanged(nameof(FanLockText));
            }
        }
    }

    /// <summary>
    /// True while the active power mode's firmware fan table owns the fans, so manual
    /// writes are refused. Mirrors the guard inside <see cref="AcerWmiManager.SetFansAsync"/>.
    /// </summary>
    public bool IsFanControlLocked =>
        HardwareSafetyPolicy.LocksFanControl(ActivePowerProfile) &&
        SettingsManager.GetInt("ExpertMode", 0) != 1;

    public bool IsFanControlEnabled => !IsFanControlLocked;

    public Visibility FanLockVisibility => IsFanControlLocked ? Visibility.Visible : Visibility.Collapsed;

    public string FanLockText => $"{ActivePowerProfile.ToString().ToUpperInvariant()} LOCKED";

    public string FanLockTooltip =>
        "This power mode runs the firmware fan table. Enable Expert Mode in Settings to force manual speeds.";

    private bool _isOverclockModeAvailable;

    /// <summary>
    /// True when Performance/Turbo may be written right now. Mirrors the gate inside
    /// <see cref="AcerWmiManager.ApplyPowerProfileAsync"/> so the rail cannot offer a
    /// mode the firmware will refuse.
    /// </summary>
    public bool IsOverclockModeAvailable => _isOverclockModeAvailable;

    public Visibility PowerModeLockVisibility =>
        _isOverclockModeAvailable ? Visibility.Collapsed : Visibility.Visible;

    public string PowerModeLockText
    {
        get
        {
            if (_isOverclockModeAvailable) return string.Empty;

            bool onAc = AcerWmiManager.GetPowerState().OnAc;
            return onAc
                ? $"BELOW {HardwareSafetyPolicy.MinimumBatteryPercentForOverclock}% · PERFORMANCE AND TURBO LOCKED"
                : "ON BATTERY · PERFORMANCE AND TURBO LOCKED";
        }
    }

    public string PowerModeLockTooltip =>
        $"Performance and Turbo need AC power. On AC they also need {HardwareSafetyPolicy.MinimumBatteryPercentForOverclock}% charge unless Expert Mode is on.";

    public string PerformanceTooltip =>
        _isOverclockModeAvailable
            ? "More power. Processor at 100% on AC with CPU boost on (99% on battery, boost off)."
            : (AcerWmiManager.GetPowerState().OnAc
                ? $"Performance locked below {HardwareSafetyPolicy.MinimumBatteryPercentForOverclock}% battery: connect AC and charge (bypass in Settings > Expert Mode)."
                : "Performance locked on battery: connect AC charger to enable.");

    public string TurboTooltip =>
        _isOverclockModeAvailable
            ? "Maximum power. Processor locked at 100% min/max on AC with CPU boost on, fans at full cooling."
            : (AcerWmiManager.GetPowerState().OnAc
                ? $"Turbo locked below {HardwareSafetyPolicy.MinimumBatteryPercentForOverclock}% battery: connect AC and charge (bypass in Settings > Expert Mode)."
                : "Turbo locked on battery: connect AC charger to enable.");

    private bool _isEcoModeAvailable;

    public bool IsEcoModeAvailable => _isEcoModeAvailable;

    public string EcoTooltip =>
        _isEcoModeAvailable
            ? "Maximum battery saving. Available on battery only, processor capped at 70%, fans quiet."
            : "Eco mode locked on AC: disconnect AC charger to enable.";

    /// <summary>Recomputes the AC/charge gate. Cheap: one cached power-status read.</summary>
    public void RefreshPowerModeAvailability()
    {
        bool available = AcerWmiManager.IsOverclockModeAvailable();
        bool changed = _isOverclockModeAvailable != available;
        _isOverclockModeAvailable = available;

        if (changed)
        {
            OnPropertyChanged(nameof(IsOverclockModeAvailable));
            OnPropertyChanged(nameof(PowerModeLockVisibility));
        }
        OnPropertyChanged(nameof(PowerModeLockText));
        OnPropertyChanged(nameof(PerformanceTooltip));
        OnPropertyChanged(nameof(TurboTooltip));

        bool onAc = AcerWmiManager.GetPowerState().OnAc;
        bool ecoAvailable = !onAc;
        bool ecoChanged = _isEcoModeAvailable != ecoAvailable;
        _isEcoModeAvailable = ecoAvailable;
        if (ecoChanged)
        {
            OnPropertyChanged(nameof(IsEcoModeAvailable));
        }
        OnPropertyChanged(nameof(EcoTooltip));
    }

    public IReadOnlyList<string> FanRampOptions => FanRamp.Options;

    private string _fanRampDown = SettingsManager.GetString("FanRampDown", FanRamp.Balanced);

    public string FanRampDown
    {
        get => _fanRampDown;
        set
        {
            if (Array.IndexOf(FanRamp.Options, value) < 0) return;
            if (SetProperty(ref _fanRampDown, value))
            {
                SettingsManager.Save("FanRampDown", value);
            }
        }
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
            _ = AcerWmiManager.SetFansAsync(FanProfile.Medium, CpuFanSpeed, GpuFanSpeed);
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

                int cpuSpd = FanCurveHelper.GetClampedSpeed(cpuCurve, currentCpuTemp);
                int gpuSpd = FanCurveHelper.GetClampedSpeed(gpuCurve, currentGpuTemp);

                await AcerWmiManager.SetFansAsync(FanProfile.Medium, cpuSpd, gpuSpd);
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
                    // The firmware refuses these modes on battery (and below the charge
                    // floor). Resolve first so the UI never shows a mode that did not land.
                    var resolved = AcerWmiManager.ResolvePowerProfileRequest(mode);
                    if (resolved != mode)
                    {
                        LogManager.Warn(
                            $"{mode} downgraded to {resolved}: AC power or charge level not met", "DashboardVM");
                    }

                    ActivePowerProfile = resolved;
                    DismissGpuAlert();

                    if (ScenarioStore.IsEnabled && !ScenarioStore.IsSuspended)
                    {
                        ScenarioStore.SetSuspended(true);
                        LogManager.Info("Scenario suspended by manual power change in Dashboard", "ScenarioVM");
                    }

                    SettingsManager.Save("LastPowerMode", (int)resolved);
                    _ = AcerWmiManager.ApplyPowerProfileAsync(resolved);
                    bool isOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                    SettingsManager.Save(isOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)resolved);

                    if (ManageCpuPower)
                    {
                        _ = CpuPowerManager.ApplyProfileLimitsAsync(resolved, isOnline);
                    }

                    await _gpuManager.ApplyProfileOcAsync(resolved, isOnline);
                    var (profileCore, profileMemory) = _gpuManager.ResolveProfileOffsets(resolved);
                    GpuCoreOffset = profileCore;
                    GpuMemoryOffset = profileMemory;

                    if (IsCustomFanEnabled && IsCurveModeEnabled)
                    {
                        ApplyCurrentFanCurve();
                    }

                    RefreshPowerModeAvailability();
                    TrayApplication.Instance?.TriggerProfileOsd(resolved);
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
                if (!AcerWmiManager.IsFanControlAllowed())
                {
                    LogManager.Warn($"Fan control is disabled in {ActivePowerProfile} mode", "ScenarioVM");
                    return;
                }

                if (ScenarioStore.IsEnabled && !ScenarioStore.IsSuspended)
                {
                    ScenarioStore.SetSuspended(true);
                    LogManager.Info("Scenario suspended by manual fan change in Dashboard", "ScenarioVM");
                }

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
                        _ = AcerWmiManager.SetFansAsync(FanProfile.Medium, CpuFanSpeed, GpuFanSpeed);
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
