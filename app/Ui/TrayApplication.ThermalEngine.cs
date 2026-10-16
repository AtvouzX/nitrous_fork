using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;

namespace Nitrous.Ui;

public partial class TrayApplication : ApplicationContext
{
    private PowerProfile? _cachedProfile;
    private List<System.Windows.Point>? _cachedCpuCurve;
    private List<System.Windows.Point>? _cachedGpuCurve;
    private long _cachedCurveVersion = -1;
    private string _lastObservedFanMode = "";
    private string _lastObservedFanPreset = "";
    private bool _lastObservedCurveEnabled = false;
    private int _cpuDownstepHoldTicks;
    private int _gpuDownstepHoldTicks;
    public const int HysteresisHoldCycles = 2; // 2 cycles * 2s = 4s delay before stepping down

    public static string FormatTrayTooltip(int cpuTemp, int gpuTemp, bool gpuIsAwake = true)
    {
        string gpuTip = (gpuIsAwake && gpuTemp > 0) ? $"{gpuTemp}°C" : "Sleep";
        string tipText = $"Nitrous | CPU: {cpuTemp}°C  GPU: {gpuTip}";
        return tipText.Length > 63 ? tipText[..63] : tipText;
    }

    private void StartBackgroundEngine()
    {
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            int tickCounter = 0;

            while (!_engineCts.Token.IsCancellationRequested)
            {
                try
                {
                    tickCounter++;

                    // 1. Detect discrete GPU wake state transitions and auto-apply clock offsets on AC.
                    // Bypassed completely if the user disabled AutoApplyGpuClocks or is on battery (DC) power.
                    bool isAcOnline = NvidiaGpuManager.IsAcPowerConnected();
                    int autoApplySetting = SettingsManager.Get("AutoApplyGpuClocks", 1);
                    bool isAutoApplyActive = isAcOnline && autoApplySetting == 1;

                    if (isAutoApplyActive)
                    {
                        bool gpuIsAwake = NvidiaGpuManager.IsGpuAwake();
                        if (NvidiaGpuManager.ShouldAutoApplyGpuClocks(_wasGpuAwake, gpuIsAwake, isAcOnline, autoApplySetting))
                        {
                            var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                            _ = _gpuManager.AutoApplyClocksAsync(activeMode, isAcOnline);
                        }
                        else if (NvidiaGpuManager.ShouldResetGpuHandle(_wasGpuAwake, gpuIsAwake))
                        {
                            _gpuManager.ResetGpuHandle();
                        }
                        _wasGpuAwake = gpuIsAwake;
                    }
                    else if (_wasGpuAwake)
                    {
                        _wasGpuAwake = false;
                        _gpuManager.ResetGpuHandle();
                    }

                    // 2. Check if the user wants Custom Fan Mode and if the Curve is enabled
                    string currentFanMode = SettingsManager.Get("LastFanMode", "Auto");
                    bool isCurveEnabled = SettingsManager.Get("IsCurveModeEnabled", 0) == 1;
                    bool isCurveActive = currentFanMode == "Medium" && isCurveEnabled;
                    long currentCurveVersion = SettingsManager.Get("FanCurveVersion", 0L);

                    // Reset tracking state if fan mode or curve override changed
                    if (currentFanMode != _lastObservedFanMode || isCurveEnabled != _lastObservedCurveEnabled)
                    {
                        _lastObservedFanMode = currentFanMode;
                        _lastObservedCurveEnabled = isCurveEnabled;
                        _lastAppliedCpuSpeed = -1;
                        _lastAppliedGpuSpeed = -1;
                        _cpuDownstepHoldTicks = 0;
                        _gpuDownstepHoldTicks = 0;
                    }

                    // If curve is active: poll telemetry and evaluate curves every 2s (1 tick).
                    // If curve is NOT active: hardware EC handles fan speeds, so poll every 10s (every 5 ticks) for tooltip.
                    if (isCurveActive || (tickCounter % 5 == 0))
                    {
                        var telemetry = AcerWmiManager.GetSystemTelemetry();
                        int effectiveGpuTemp = telemetry.GpuTemp;
                        bool gpuAwakeForTooltip = isAutoApplyActive ? _wasGpuAwake : (effectiveGpuTemp > 0);
                        string truncatedTip = FormatTrayTooltip(telemetry.CpuTemp, effectiveGpuTemp, gpuAwakeForTooltip);
                        if (trayIcon.Text != truncatedTip)
                        {
                            trayIcon.Text = truncatedTip;
                        }

                        if (isCurveActive && telemetry.CpuTemp > 0)
                        {
                            // 3. Load curves from cache or Registry if profile changed or curve updated
                            var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
                            string pName = SettingsManager.Get("ActiveFanCurvePreset", "Auto");
                            if (pName == "Auto") pName = activeMode.ToString();

                            if (_cachedCpuCurve == null || _cachedGpuCurve == null || _cachedProfile != activeMode || _lastObservedFanPreset != pName || _cachedCurveVersion != currentCurveVersion)
                            {
                                _cachedProfile = activeMode;
                                _lastObservedFanPreset = pName;
                                _cachedCurveVersion = currentCurveVersion;
                                _cachedCpuCurve = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{pName}", FanCurveHelper.GetDefaultCpuCurve(activeMode));
                                _cachedGpuCurve = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{pName}", FanCurveHelper.GetDefaultGpuCurve(activeMode));
                                _lastAppliedCpuSpeed = -1;
                                _lastAppliedGpuSpeed = -1;
                            }

                            // 4. Interpolate raw target speeds
                            int targetCpuSpeed = Math.Clamp(FanCurveHelper.InterpolateSpeed(_cachedCpuCurve, telemetry.CpuTemp), 0, 100);
                            int targetGpuSpeed = effectiveGpuTemp == 0
                                ? targetCpuSpeed
                                : Math.Clamp(FanCurveHelper.InterpolateSpeed(_cachedGpuCurve, effectiveGpuTemp), 0, 100);

                            // 5. Apply Hysteresis (Anti-Revving):
                            // Ramp-up: Immediate (protects hardware)
                            // Ramp-down: Hold for HysteresisHoldCycles (4s) before stepping down
                            int appliedCpuSpeed = _lastAppliedCpuSpeed;
                            if (_lastAppliedCpuSpeed == -1 || targetCpuSpeed >= _lastAppliedCpuSpeed)
                            {
                                appliedCpuSpeed = targetCpuSpeed;
                                _cpuDownstepHoldTicks = 0;
                            }
                            else
                            {
                                _cpuDownstepHoldTicks++;
                                if (_cpuDownstepHoldTicks >= HysteresisHoldCycles)
                                {
                                    appliedCpuSpeed = targetCpuSpeed;
                                    _cpuDownstepHoldTicks = 0;
                                }
                            }

                            int appliedGpuSpeed = _lastAppliedGpuSpeed;
                            if (_lastAppliedGpuSpeed == -1 || targetGpuSpeed >= _lastAppliedGpuSpeed)
                            {
                                appliedGpuSpeed = targetGpuSpeed;
                                _gpuDownstepHoldTicks = 0;
                            }
                            else
                            {
                                _gpuDownstepHoldTicks++;
                                if (_gpuDownstepHoldTicks >= HysteresisHoldCycles)
                                {
                                    appliedGpuSpeed = targetGpuSpeed;
                                    _gpuDownstepHoldTicks = 0;
                                }
                            }

                            // 6. Fire WMI only if changed
                            if (appliedCpuSpeed != _lastAppliedCpuSpeed || appliedGpuSpeed != _lastAppliedGpuSpeed)
                            {
                                _lastAppliedCpuSpeed = appliedCpuSpeed;
                                _lastAppliedGpuSpeed = appliedGpuSpeed;
                                await AcerWmiManager.SetCustomFansAsync(appliedCpuSpeed, appliedGpuSpeed);
                            }
                        }
                    }

                    // Periodic memory purge every ~60 seconds (every 30 ticks)
                    if (tickCounter % 30 == 0)
                    {
                        GC.Collect(1, GCCollectionMode.Default, false);
                        MemoryHelper.TrimWorkingSet();
                    }
                }
                catch { /* Absorb exceptions to keep background engine alive */ }

                await timer.WaitForNextTickAsync(_engineCts.Token);
            }
        });
    }
}
