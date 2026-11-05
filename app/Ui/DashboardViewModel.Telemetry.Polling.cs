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
                    // Debounced via UpdateSleepState with the same 4-cycle hold as Tray to absorb
                    // transient RTD3 drops and CM API read flickers.
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
                            smi = await _gpuManager.GetNvmlTelemetryAsync(token, gpuIsAwake);
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
                        RefreshPowerModeAvailability();

                        // 1. Update WMI CPU/GPU Telemetry
                        CpuTempText = telemetry.CpuTemp > 0 ? $"{telemetry.CpuTemp} C" : "-- C";
                        CpuRpmText = telemetry.CpuRpm > 0 ? $"{telemetry.CpuRpm} RPM" : "-- RPM";
                        CpuTempColor = telemetry.CpuTemp > 85 ? "#FF453A" : "White";

                        GpuTempText = telemetry.GpuTemp > 0 ? $"{telemetry.GpuTemp} C" : "-- C";
                        GpuRpmText = telemetry.GpuRpm > 0 ? $"{telemetry.GpuRpm} RPM" : "-- RPM";
                        GpuTempColor = telemetry.GpuTemp > 85 ? "#FF453A" : "White";

                        // Self-calibrate per-fan maximum RPM ceiling when a new high-water mark is observed
                        int calibratedMaxCpuRpm = FanCurveHelper.ObserveAndGetCalibratedMaxRpm(
                            FanCurveHelper.MaxCpuRpmKey, telemetry.CpuRpm);
                        int calibratedMaxGpuRpm = FanCurveHelper.ObserveAndGetCalibratedMaxRpm(
                            FanCurveHelper.MaxGpuRpmKey, telemetry.GpuRpm);

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
                                CpuFanPercentage = FanCurveHelper.RpmToPercentage(telemetry.CpuRpm, calibratedMaxCpuRpm);
                                GpuFanPercentage = FanCurveHelper.RpmToPercentage(telemetry.GpuRpm, calibratedMaxGpuRpm);
                            }
                        }

                        // 2. Update NVIDIA SMI Deep Telemetry
                        if (smi is { IsDisplayable: true })
                        {
                            // Override WMI GPU temp with highly-accurate NVML temp if awake
                            if (smi.CoreTemp > 0)
                            {
                                GpuTempText = $"{smi.CoreTemp} C";
                                GpuTempColor = smi.CoreTemp >= 85 ? "#FF453A" : "White";
                            }

                            GpuNameText = GetCachedGpuDisplayName(smi.Name);
                            GpuArchText = GetCachedGpuArchDisplay(smi.Architecture);

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

                        bool isSmiValid = smi?.IsDisplayable == true;

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
                            // Holding through a transient drop: keep last good sample, draw no zero.
                            GpuGraphOpacity = 1.0;
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
}
