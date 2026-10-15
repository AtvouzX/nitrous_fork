using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Nitrous.Enums;
using Nitrous.Helpers;
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
    public PointCollection CpuTempPoints { get => _cpuTempPoints; set => SetProperty(ref _cpuTempPoints, value); }

    private PointCollection _cpuTempFillPoints = new PointCollection();
    public PointCollection CpuTempFillPoints { get => _cpuTempFillPoints; set => SetProperty(ref _cpuTempFillPoints, value); }

    private PointCollection _cpuUsagePoints = new PointCollection();
    public PointCollection CpuUsagePoints { get => _cpuUsagePoints; set => SetProperty(ref _cpuUsagePoints, value); }

    private PointCollection _cpuUsageFillPoints = new PointCollection();
    public PointCollection CpuUsageFillPoints { get => _cpuUsageFillPoints; set => SetProperty(ref _cpuUsageFillPoints, value); }

    private PointCollection _gpuTempPoints = new PointCollection();
    public PointCollection GpuTempPoints { get => _gpuTempPoints; set => SetProperty(ref _gpuTempPoints, value); }

    private PointCollection _gpuTempFillPoints = new PointCollection();
    public PointCollection GpuTempFillPoints { get => _gpuTempFillPoints; set => SetProperty(ref _gpuTempFillPoints, value); }

    private PointCollection _gpuUsagePoints = new PointCollection();
    public PointCollection GpuUsagePoints { get => _gpuUsagePoints; set => SetProperty(ref _gpuUsagePoints, value); }

    private PointCollection _gpuUsageFillPoints = new PointCollection();
    public PointCollection GpuUsageFillPoints { get => _gpuUsageFillPoints; set => SetProperty(ref _gpuUsageFillPoints, value); }

    private double _gpuGraphOpacity = 1.0;

    public double GpuGraphOpacity
    {
        get => _gpuGraphOpacity;
        set => SetProperty(ref _gpuGraphOpacity, value);
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
        GpuCoreClockText = "-- MHz";
        GpuMemClockText = "-- MHz";
        GpuPowerText = "-- W";
    }

    private Visibility _gpuSleepOverlayVisibility = Visibility.Collapsed;

    public Visibility GpuSleepOverlayVisibility
    {
        get => _gpuSleepOverlayVisibility;
        set => SetProperty(ref _gpuSleepOverlayVisibility, value);
    }

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
        var (found, point, diff) = GraphGeometryHelper.FindHoverPoint(_cpuHistory, pixelX, actualWidth, now);

        if (found)
        {
            CpuHoverVisibility = Visibility.Visible;
            CpuHoverMargin = new Thickness(pixelX, 0, 0, 0);
            CpuTooltipMargin = new Thickness(pixelX > (actualWidth / 2) ? pixelX - 85 : pixelX + 5, 5, 0, 0);
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
        var (found, point, diff) = GraphGeometryHelper.FindHoverPoint(_gpuHistory, pixelX, actualWidth, now);

        if (found)
        {
            GpuHoverVisibility = Visibility.Visible;
            GpuHoverMargin = new Thickness(pixelX, 0, 0, 0);
            GpuTooltipMargin = new Thickness(pixelX > (actualWidth / 2) ? pixelX - 85 : pixelX + 5, 5, 0, 0);
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

    private bool _wasGpuAwake;

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

                    // Check if discrete GPU is awake before querying NVML
                    bool gpuIsAwake = NvidiaGpuManager.IsGpuAwake();
                    if (!_wasGpuAwake && gpuIsAwake)
                    {
                        if (NvidiaGpuManager.IsAcPowerConnected() && SettingsManager.Get("AutoApplyGpuClocks", 1) == 1)
                        {
                            _ = AutoApplyGpuClocksAsync();
                        }
                    }
                    _wasGpuAwake = gpuIsAwake;

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
                            ClearDeepTelemetryUI();
                            GpuGraphOpacity = 0.3;
                            GpuUsageText = "Sleep";
                            _gpuHistory.Add(new TelemetryPoint { Timestamp = now, Temp = 0, Usage = 0 });
                        }

                        GpuSleepOverlayVisibility = gpuIsAwake
                            ? Visibility.Collapsed
                            : Visibility.Visible;

                        // Prune > 5 mins (300,000 ms)
                        _cpuHistory.RemoveAll(p => now - p.Timestamp > 300000);
                        _gpuHistory.RemoveAll(p => now - p.Timestamp > 300000);

                        var maxCpuTemp = _cpuHistory.Count > 0 ? _cpuHistory.Max(p => p.Temp) : 0;
                        var maxCpuUsage = _cpuHistory.Count > 0 ? _cpuHistory.Max(p => p.Usage) : 0;
                        CpuMaxStatsText = $"Max: {maxCpuTemp}°C  {maxCpuUsage:0}%";

                        var maxGpuTemp = _gpuHistory.Count > 0 ? _gpuHistory.Max(p => p.Temp) : 0;
                        var maxGpuUsage = _gpuHistory.Count > 0 ? _gpuHistory.Max(p => p.Usage) : 0;
                        GpuMaxStatsText = $"Max: {maxGpuTemp}°C  {maxGpuUsage:0}%";

                        CpuTempPoints = GraphGeometryHelper.GenerateGraphPoints(_cpuHistory, true, now);
                        CpuTempFillPoints = GraphGeometryHelper.GenerateGraphFillPoints(_cpuHistory, true, now);
                        CpuUsagePoints = GraphGeometryHelper.GenerateGraphPoints(_cpuHistory, false, now);
                        CpuUsageFillPoints = GraphGeometryHelper.GenerateGraphFillPoints(_cpuHistory, false, now);
                        GpuTempPoints = GraphGeometryHelper.GenerateGraphPoints(_gpuHistory, true, now);
                        GpuTempFillPoints = GraphGeometryHelper.GenerateGraphFillPoints(_gpuHistory, true, now);
                        GpuUsagePoints = GraphGeometryHelper.GenerateGraphPoints(_gpuHistory, false, now);
                        GpuUsageFillPoints = GraphGeometryHelper.GenerateGraphFillPoints(_gpuHistory, false, now);
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
}
