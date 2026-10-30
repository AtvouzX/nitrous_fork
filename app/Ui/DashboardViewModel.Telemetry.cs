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

    private string _batteryBadgeForeground = "#8A8A96";

    public string BatteryBadgeForeground
    {
        get => _batteryBadgeForeground;
        set => SetProperty(ref _batteryBadgeForeground, value);
    }

    private string _batteryBadgeBackground = "#121215";

    public string BatteryBadgeBackground
    {
        get => _batteryBadgeBackground;
        set => SetProperty(ref _batteryBadgeBackground, value);
    }

    private string _batteryBadgeBorderBrush = "#26262B";

    public string BatteryBadgeBorderBrush
    {
        get => _batteryBadgeBorderBrush;
        set => SetProperty(ref _batteryBadgeBorderBrush, value);
    }

    private int _lastBatteryRate = int.MinValue;
    private uint _lastBatteryMaxCapacity;
    private uint _lastBatteryRemainingCapacity;
    private bool _lastBatteryAc;
    private bool _lastBatteryPresent;
    private bool _lastBatteryCharging;

    public void UpdateBatteryBadge(Powrprof.SYSTEM_BATTERY_STATE batteryState)
    {
        if (batteryState.MaxCapacity > 0 &&
            batteryState.Rate == _lastBatteryRate &&
            batteryState.MaxCapacity == _lastBatteryMaxCapacity &&
            batteryState.RemainingCapacity == _lastBatteryRemainingCapacity &&
            batteryState.AcOnLine == _lastBatteryAc &&
            batteryState.BatteryPresent == _lastBatteryPresent &&
            batteryState.Charging == _lastBatteryCharging)
        {
            return;
        }

        _lastBatteryRate = batteryState.Rate;
        _lastBatteryMaxCapacity = batteryState.MaxCapacity;
        _lastBatteryRemainingCapacity = batteryState.RemainingCapacity;
        _lastBatteryAc = batteryState.AcOnLine;
        _lastBatteryPresent = batteryState.BatteryPresent;
        _lastBatteryCharging = batteryState.Charging;

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
                BatteryBadgeForeground = "#E85D2A";
                BatteryBadgeBackground = "#121215";
                BatteryBadgeBorderBrush = "#26262B";
            }
            else
            {
                BatteryBadgeText = "CHARGED";
                BatteryBadgeForeground = "#EDEDEF";
                BatteryBadgeBackground = "#121215";
                BatteryBadgeBorderBrush = "#26262B";
            }
        }
        else
        {
            BatteryBadgeText = watts > 0.1
                ? $"BATTERY • {watts.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}W"
                : "BATTERY";
            BatteryBadgeForeground = "#EDEDEF";
            BatteryBadgeBackground = "#18181C";
            BatteryBadgeBorderBrush = "#26262B";
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

    private string? _cachedGpuRawName;
    private string _cachedGpuDisplayName = "NVIDIA GPU";
    private string? _cachedGpuRawArch;
    private string _cachedGpuArchDisplay = "";

    // Ticket 8: per-tick cache for GPU display strings. NVML name/arch are
    // constant across ticks; this trims one Trim/EndsWith/ToUpper pair per tick.
    public string GetCachedGpuDisplayName(string raw)
    {
        if (raw.Equals(_cachedGpuRawName, StringComparison.Ordinal))
        {
            return _cachedGpuDisplayName;
        }

        _cachedGpuRawName = raw;
        _cachedGpuDisplayName = FormatGpuName(raw).ToUpperInvariant();
        return _cachedGpuDisplayName;
    }

    public string GetCachedGpuArchDisplay(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        if (raw.Equals(_cachedGpuRawArch, StringComparison.Ordinal))
        {
            return _cachedGpuArchDisplay;
        }

        _cachedGpuRawArch = raw;
        _cachedGpuArchDisplay = raw.ToUpperInvariant();
        return _cachedGpuArchDisplay;
    }

    public static string FormatGpuName(string name)
    {
        var display = name.Trim();

        if (display.EndsWith(" GPU", StringComparison.OrdinalIgnoreCase))
            display = display[..^" GPU".Length].TrimEnd();

        return string.IsNullOrWhiteSpace(display) ? "NVIDIA GPU" : display;
    }
}
