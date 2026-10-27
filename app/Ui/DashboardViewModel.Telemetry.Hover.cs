using System;
using System.Windows;
using Nitrous.Helpers;

namespace Nitrous.Ui;

public partial class DashboardViewModel
{
    // --- CPU HOVER PROPERTIES & STATE ---
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

    // --- GPU HOVER PROPERTIES & STATE ---
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
            CpuHoverTime = $"-{(int)diff.TotalMinutes}M {(int)diff.Seconds}S";
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
            GpuHoverTime = $"-{(int)diff.TotalMinutes}M {(int)diff.Seconds}S";
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
}
