using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Interop;
using Nitrous.Managers;
using Point = System.Windows.Point;
using Application = System.Windows.Application;

namespace Nitrous.Ui;

public partial class DashboardViewModel
{
    public struct TelemetryPoint
    {
        public long Timestamp;
        public double Temp;
        public double Usage;
    }

    private List<TelemetryPoint> _cpuHistory = new();
    private List<TelemetryPoint> _gpuHistory = new();

    public IReadOnlyList<TelemetryPoint> CpuHistory => _cpuHistory;
    public IReadOnlyList<TelemetryPoint> GpuHistory => _gpuHistory;

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

    private double _cpuGraphWidth = GraphGeometryHelper.DefaultGraphWidth;

    public double CpuGraphWidth
    {
        get => _cpuGraphWidth;
        set => SetProperty(ref _cpuGraphWidth, value);
    }

    private double _cpuGraphHeight = GraphGeometryHelper.DefaultGraphHeight;

    public double CpuGraphHeight
    {
        get => _cpuGraphHeight;
        set => SetProperty(ref _cpuGraphHeight, value);
    }

    private double _gpuGraphWidth = GraphGeometryHelper.DefaultGraphWidth;

    public double GpuGraphWidth
    {
        get => _gpuGraphWidth;
        set => SetProperty(ref _gpuGraphWidth, value);
    }

    private double _gpuGraphHeight = GraphGeometryHelper.DefaultGraphHeight;

    public double GpuGraphHeight
    {
        get => _gpuGraphHeight;
        set => SetProperty(ref _gpuGraphHeight, value);
    }

    public void UpdateCpuGraphDimensions(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        if (Math.Abs(_cpuGraphWidth - width) > 0.1 || Math.Abs(_cpuGraphHeight - height) > 0.1)
        {
            _cpuGraphWidth = width;
            _cpuGraphHeight = height;
            RegenerateCpuGraphPoints();
        }
    }

    public void UpdateGpuGraphDimensions(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        if (Math.Abs(_gpuGraphWidth - width) > 0.1 || Math.Abs(_gpuGraphHeight - height) > 0.1)
        {
            _gpuGraphWidth = width;
            _gpuGraphHeight = height;
            RegenerateGpuGraphPoints();
        }
    }

    public void RegenerateCpuGraphPoints(long now = 0)
    {
        if (now == 0) now = Environment.TickCount64;
        CpuTempPoints =
            GraphGeometryHelper.GenerateGraphPoints(_cpuHistory, true, now, _cpuGraphWidth, _cpuGraphHeight);
        CpuTempFillPoints =
            GraphGeometryHelper.GenerateGraphFillPoints(_cpuHistory, true, now, _cpuGraphWidth, _cpuGraphHeight);
        CpuUsagePoints =
            GraphGeometryHelper.GenerateGraphPoints(_cpuHistory, false, now, _cpuGraphWidth, _cpuGraphHeight);
        CpuUsageFillPoints =
            GraphGeometryHelper.GenerateGraphFillPoints(_cpuHistory, false, now, _cpuGraphWidth, _cpuGraphHeight);
    }

    public void RegenerateGpuGraphPoints(long now = 0)
    {
        if (now == 0) now = Environment.TickCount64;
        GpuTempPoints =
            GraphGeometryHelper.GenerateGraphPoints(_gpuHistory, true, now, _gpuGraphWidth, _gpuGraphHeight);
        GpuTempFillPoints =
            GraphGeometryHelper.GenerateGraphFillPoints(_gpuHistory, true, now, _gpuGraphWidth, _gpuGraphHeight);
        GpuUsagePoints =
            GraphGeometryHelper.GenerateGraphPoints(_gpuHistory, false, now, _gpuGraphWidth, _gpuGraphHeight);
        GpuUsageFillPoints =
            GraphGeometryHelper.GenerateGraphFillPoints(_gpuHistory, false, now, _gpuGraphWidth, _gpuGraphHeight);
    }

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

    public const string AcBadgeIcon =
        "M16,7V3H14V7H10V3H8V7C8,10 9.79,11.4 11,11.83V16H13V11.83C14.21,11.4 16,10 16,7M10,18H14V22H10V18Z";

    public const string BattBadgeIcon =
        "M16.67,4H15V2H9V4H7.33A1.33,1.33 0 0,0 6,5.33V20.67C6,21.4 6.6,22 7.33,22H16.67A1.33,1.33 0 0,0 18,20.67V5.33C18,4.6 17.4,4 16.67,4Z";

    private double _batteryChargePercent = 100.0;

    public double BatteryChargePercent
    {
        get => _batteryChargePercent;
        set => SetProperty(ref _batteryChargePercent, value);
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

    private string _batteryBadgeIcon = AcBadgeIcon;

    public string BatteryBadgeIcon
    {
        get => _batteryBadgeIcon;
        set => SetProperty(ref _batteryBadgeIcon, value);
    }

    public void UpdateBatteryBadge(Powrprof.SYSTEM_BATTERY_STATE batteryState)
    {
        double watts = UnitConverter.MwToW(Math.Abs((double)batteryState.Rate));

        if (batteryState.MaxCapacity > 0)
        {
            BatteryChargePercent = UnitConverter.BatteryPercent(batteryState.RemainingCapacity, batteryState.MaxCapacity);
        }
        else
        {
            var pStatus = System.Windows.Forms.SystemInformation.PowerStatus;
            BatteryChargePercent = UnitConverter.BatteryPercentFromFraction(pStatus.BatteryLifePercent);
        }

        if (!batteryState.BatteryPresent || batteryState.AcOnLine)
        {
            if (batteryState.BatteryPresent && batteryState.Charging && watts > 0.5)
            {
                BatteryBadgeText =
                    $"CHARGING • {watts.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}W";
                BatteryBadgeBorder = "#34C759";
                BatteryBadgeForeground = "#34C759";
                BatteryBadgeIcon = AcBadgeIcon;
            }
            else
            {
                BatteryBadgeText = "AC POWER";
                BatteryBadgeBorder = "#FF453A";
                BatteryBadgeForeground = "White";
                BatteryBadgeIcon = AcBadgeIcon;
            }
        }
        else
        {
            BatteryBadgeText = watts > 0.1
                ? $"BATTERY • {watts.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}W"
                : "BATTERY";
            BatteryBadgeBorder = "#32D74B";
            BatteryBadgeForeground = "#64D2FF";
            BatteryBadgeIcon = BattBadgeIcon;
        }
    }

    public void UpdateBatteryBadge()
    {
        var state = BatteryManager.GetBatteryState();
        UpdateBatteryBadge(state);
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

    private bool _autoApplyGpuClocks = true;

    public bool AutoApplyGpuClocks
    {
        get => _autoApplyGpuClocks;
        set
        {
            if (SetProperty(ref _autoApplyGpuClocks, value))
            {
                SettingsManager.Save("AutoApplyGpuClocks", value ? 1 : 0);
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
        GpuCoreClockText = "-- MHZ";
        GpuMemClockText = "-- MHZ";
        GpuPowerText = "-- W";
    }

    private Visibility _gpuSleepOverlayVisibility = Visibility.Collapsed;

    public Visibility GpuSleepOverlayVisibility
    {
        get => _gpuSleepOverlayVisibility;
        set => SetProperty(ref _gpuSleepOverlayVisibility, value);
    }

    private bool _wasGpuAwake;
    private int _gpuSleepHoldTicks;
    internal int GpuSleepHoldTicks => _gpuSleepHoldTicks;

    private PowerProfile? _cachedProfile;
    private List<Point>? _cachedCpuCurve;
    private List<Point>? _cachedGpuCurve;
    private long _cachedCurveVersion = -1;
    private string _lastObservedFanPreset = "";

    internal long CachedCurveVersionForTesting => _cachedCurveVersion;
    internal List<Point>? CachedCpuCurveForTesting => _cachedCpuCurve;
    internal List<Point>? CachedGpuCurveForTesting => _cachedGpuCurve;

    internal void EnsureCachedCurvesLoaded(long currentCurveVersion, string pName)
    {
        if (_cachedCpuCurve == null || _cachedGpuCurve == null || _cachedProfile != ActivePowerProfile ||
            _lastObservedFanPreset != pName || _cachedCurveVersion != currentCurveVersion)
        {
            _cachedProfile = ActivePowerProfile;
            _lastObservedFanPreset = pName;
            _cachedCurveVersion = currentCurveVersion;
            _cachedCpuCurve = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{pName}",
                FanCurveHelper.GetDefaultCpuCurve(ActivePowerProfile));
            _cachedGpuCurve = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{pName}",
                FanCurveHelper.GetDefaultGpuCurve(ActivePowerProfile));
        }
    }

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
                    // Non-UI Work: Fetching WMI & SMI telemetry synchronously on ThreadPool
                    var batteryState = BatteryManager.GetBatteryState();
                    var telemetry = AcerWmiManager.GetSystemTelemetry();

                    float cpuUsage = 0;
                    try
                    {
                        if (_cpuUsageCounter != null)
                        {
                            cpuUsage = _cpuUsageCounter.NextValue();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Debug($"CPU counter read failed: {ex.Message}", "DashboardVM");
                    }

                    // Check if discrete GPU is awake before querying NVML.
                    // Background Tray is the sole owner of auto-apply on wake; this loop never auto-applies.
                    // Debounced via UpdateSleepState to absorb transient 1-2s RTD3 drops and CM API read flickers.
                    // Power gating excluded here (forced AC/enabled) so the overlay tracks physical
                    // sleep state on battery too; only ResetHandle acted upon, never AutoApply.
                    bool gpuIsAwake = NvidiaGpuManager.IsGpuAwake();
                    var (newWasAwake, newHoldTicks, sleepAction) =
                        NvidiaGpuManager.UpdateSleepState(_wasGpuAwake, _gpuSleepHoldTicks, gpuIsAwake, true, 1);

                    _wasGpuAwake = newWasAwake;
                    _gpuSleepHoldTicks = newHoldTicks;

                    if (sleepAction == NvidiaGpuManager.GpuSleepAction.ResetHandle)
                    {
                        _gpuManager.ResetGpuHandle();
                    }

                    NvidiaGpuManager.GpuTelemetry? smi = null;

                    if (gpuIsAwake)
                    {
                        try
                        {
                            smi = await _gpuManager.GetNvmlTelemetryAsync(token);
                        }
                        catch (Exception ex)
                        {
                            LogManager.Debug($"NVML telemetry read failed: {ex.Message}", "DashboardVM");
                        }
                    }

                    if (token.IsCancellationRequested) break;

                    // UI Work: Marshal formatting and ObservableCollection updates to Dispatcher
                    _ = Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        // 0. Update Battery Telemetry
                        UpdateBatteryBadge(batteryState);

                        // 1. Update WMI CPU/GPU Telemetry
                        CpuTempText = telemetry.CpuTemp > 0 ? $"{telemetry.CpuTemp} C" : "-- C";
                        CpuRpmText = telemetry.CpuRpm > 0 ? $"{telemetry.CpuRpm} RPM" : "-- RPM";
                        CpuTempColor = telemetry.CpuTemp > 85 ? "#FF453A" : "White";

                        GpuTempText = telemetry.GpuTemp > 0 ? $"{telemetry.GpuTemp} C" : "-- C";
                        GpuRpmText = telemetry.GpuRpm > 0 ? $"{telemetry.GpuRpm} RPM" : "-- RPM";
                        GpuTempColor = telemetry.GpuTemp > 85 ? "#FF453A" : "White";

                        // Update Fan read percentage indicators
                        if (IsCustomFanEnabled && !IsCurveModeEnabled)
                        {
                            CpuFanPercentage = CpuFanSpeed;
                            GpuFanPercentage = GpuFanSpeed;
                        }
                        else
                        {
                            var activeFanMode =
                                Enum.TryParse(SettingsManager.GetString("LastFanMode", "Auto"), out FanProfile fMode)
                                    ? fMode
                                    : FanProfile.Auto;
                            if (activeFanMode == FanProfile.Max)
                            {
                                CpuFanPercentage = 100;
                                GpuFanPercentage = 100;
                            }
                            else if (activeFanMode == FanProfile.Medium && IsCurveModeEnabled)
                            {
                                int curCpu = telemetry.CpuTemp > 0 ? telemetry.CpuTemp : 50;
                                int curGpu = telemetry.GpuTemp > 0 ? telemetry.GpuTemp : curCpu;
                                long currentCurveVersion = SettingsManager.GetLong("FanCurveVersion", 0L);
                                string pName = SettingsManager.GetString("ActiveFanCurvePreset", "Auto");
                                if (pName == "Auto") pName = ActivePowerProfile.ToString();

                                EnsureCachedCurvesLoaded(currentCurveVersion, pName);

                                CpuFanPercentage = FanCurveHelper.GetClampedSpeed(_cachedCpuCurve, curCpu);
                                GpuFanPercentage = FanCurveHelper.GetClampedSpeed(_cachedGpuCurve, curGpu);
                            }
                            else
                            {
                                CpuFanPercentage = FanCurveHelper.RpmToPercentage(telemetry.CpuRpm);
                                GpuFanPercentage = FanCurveHelper.RpmToPercentage(telemetry.GpuRpm);
                            }
                        }

                        // 2. Update NVIDIA SMI Deep Telemetry
                        if (smi != null && !string.IsNullOrEmpty(smi.Name) && smi.Name != "Unknown" &&
                            smi.PState != "Sleep")
                        {
                            // Override WMI GPU temp with highly-accurate NVML temp if awake
                            if (smi.CoreTemp > 0)
                            {
                                GpuTempText = $"{smi.CoreTemp} C";
                                GpuTempColor = smi.CoreTemp >= 85 ? "#FF453A" : "White";
                            }

                            GpuNameText = FormatGpuName(smi.Name).ToUpperInvariant();
                            GpuArchText = smi.Architecture?.ToUpperInvariant() ?? "";

                            GpuLoadText = $"{smi.GpuLoad}%";
                            GpuLoadColor = smi.GpuLoad >= 95
                                ? "#FF453A"
                                : (smi.GpuLoad >= 80 ? "#FF9F0A" : "White");

                            GpuVramText = $"{smi.VramUsedMb} / {smi.VramTotalMb} MB";

                            GpuDeepTempText = $"{smi.CoreTemp} C";
                            GpuDeepTempColor =
                                smi.CoreTemp >= 85 ? "#FF453A" : (smi.CoreTemp >= 78 ? "#FF9F0A" : "White");

                            GpuPStateText = smi.PState;
                            GpuCoreClockText = $"{smi.CurrentCoreClock} MHZ";
                            GpuMemClockText = $"{smi.CurrentMemoryClock} MHZ";

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

                        // 3. Update Graphing Data
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
                        else if (newWasAwake)
                        {
                            // GPU is awake (or holding in hysteresis hold window) but NVML is momentarily unavailable
                            GpuGraphOpacity = 1.0;
                            if (GpuUsageText == "SLEEP" || GpuUsageText == "Sleep") GpuUsageText = "--%";
                            _gpuHistory.Add(new TelemetryPoint
                                { Timestamp = now, Temp = telemetry.GpuTemp, Usage = 0 });
                        }
                        else
                        {
                            // GPU is confirmed in D3/D3Cold sleep after sustained hysteresis cycles
                            ClearDeepTelemetryUI();
                            GpuGraphOpacity = 0.3;
                            GpuUsageText = "SLEEP";
                            _gpuHistory.Add(new TelemetryPoint { Timestamp = now, Temp = 0, Usage = 0 });
                        }

                        GpuSleepOverlayVisibility = newWasAwake
                            ? Visibility.Collapsed
                            : Visibility.Visible;

                        // Prune > 5 mins (300,000 ms) with zero allocations from monotonically increasing history
                        long cutoff = now - 300000;
                        while (_cpuHistory.Count > 0 && _cpuHistory[0].Timestamp < cutoff)
                        {
                            _cpuHistory.RemoveAt(0);
                        }

                        while (_gpuHistory.Count > 0 && _gpuHistory[0].Timestamp < cutoff)
                        {
                            _gpuHistory.RemoveAt(0);
                        }

                        double maxCpuTemp = 0;
                        double maxCpuUsage = 0;
                        for (int i = 0; i < _cpuHistory.Count; i++)
                        {
                            if (_cpuHistory[i].Temp > maxCpuTemp) maxCpuTemp = _cpuHistory[i].Temp;
                            if (_cpuHistory[i].Usage > maxCpuUsage) maxCpuUsage = _cpuHistory[i].Usage;
                        }

                        CpuMaxStatsText = $"MAX: {maxCpuTemp}°C  {maxCpuUsage:0}%";

                        double maxGpuTemp = 0;
                        double maxGpuUsage = 0;
                        for (int i = 0; i < _gpuHistory.Count; i++)
                        {
                            if (_gpuHistory[i].Temp > maxGpuTemp) maxGpuTemp = _gpuHistory[i].Temp;
                            if (_gpuHistory[i].Usage > maxGpuUsage) maxGpuUsage = _gpuHistory[i].Usage;
                        }

                        GpuMaxStatsText = $"MAX: {maxGpuTemp}°C  {maxGpuUsage:0}%";

                        RegenerateCpuGraphPoints(now);
                        RegenerateGpuGraphPoints(now);
                    }, System.Windows.Threading.DispatcherPriority.Background);

                    // Wait asynchronously for the next 2-second interval tick
                    await timer.WaitForNextTickAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break; // Gracefully exit when token is cancelled
                }
                catch (Exception ex)
                {
                    LogManager.Debug($"Telemetry loop exception: {ex.Message}", "DashboardVM");
                }
            }
        }, token);
    }

    public static string FormatGpuName(string name)
    {
        var display = name.Trim();

        if (display.EndsWith(" GPU", StringComparison.OrdinalIgnoreCase))
            display = display[..^" GPU".Length].TrimEnd();

        return string.IsNullOrWhiteSpace(display) ? "NVIDIA GPU" : display;
    }
}
