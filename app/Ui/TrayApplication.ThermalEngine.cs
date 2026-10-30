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
    private int _gpuSleepHoldTicks;
    internal int GpuSleepHoldTicks => _gpuSleepHoldTicks;
    public const int HysteresisHoldCycles = 2; // 2 cycles * 2s = 4s delay before stepping down

    public static string FormatTrayTooltip(int cpuTemp, int gpuTemp, bool gpuIsAwake = true)
    {
        string gpuTip = (gpuIsAwake && gpuTemp > 0) ? $"{gpuTemp}°C" : "Sleep";
        string tipText = $"Nitrous | CPU: {cpuTemp}°C  GPU: {gpuTip}";
        return tipText.Length > 63 ? tipText[..63] : tipText;
    }

    private int _lastTipCpu = int.MinValue;
    private int _lastTipGpu = int.MinValue;
    private bool _lastTipAwake;

    // Ticket 8: skip tooltip string interp when inputs unchanged. Behavior-identical:
    // tray text ends up the same, but idle ticks allocate nothing.
    public static bool TooltipInputsChanged(int cpuTemp, int gpuTemp, bool gpuAwake, int lastCpu, int lastGpu, bool lastAwake)
    {
        return cpuTemp != lastCpu || gpuTemp != lastGpu || gpuAwake != lastAwake;
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
                    // Debounced via UpdateSleepState to absorb transient 1-2s RTD3 drops and CM API read flickers.
                    bool isAcOnline = NvidiaGpuManager.IsAcPowerConnected();
                    int autoApplySetting = SettingsManager.GetInt("AutoApplyGpuClocks", 1);
                    bool isAutoApplyActive = isAcOnline && autoApplySetting == 1;

                    bool gpuIsAwake = NvidiaGpuManager.IsGpuAwake();
                    var (newWasAwake, newHoldTicks, sleepAction) =
                        NvidiaGpuManager.UpdateSleepState(_wasGpuAwake, _gpuSleepHoldTicks, gpuIsAwake, isAcOnline, autoApplySetting);

                    _wasGpuAwake = newWasAwake;
                    _gpuSleepHoldTicks = newHoldTicks;

                    if (sleepAction == NvidiaGpuManager.GpuSleepAction.AutoApply)
                    {
                        var activeMode = (PowerProfile)SettingsManager.GetInt("LastPowerMode", (int)PowerProfile.Performance);
                        _ = _gpuManager.ApplyProfileOcAsync(activeMode, isAcOnline);
                    }
                    else if (sleepAction == NvidiaGpuManager.GpuSleepAction.ResetHandle)
                    {
                        _gpuManager.ResetGpuHandle();
                    }

                    // 2. Check if the user wants Custom Fan Mode and if the Curve is enabled
                    string currentFanMode = SettingsManager.GetString("LastFanMode", "Auto");
                    bool isCurveEnabled = SettingsManager.GetInt("IsCurveModeEnabled", 0) == 1;
                    bool isCurveActive = currentFanMode == "Medium" && isCurveEnabled;
                    long currentCurveVersion = SettingsManager.GetLong("FanCurveVersion", 0L);

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
                        _ = FanCurveHelper.ObserveAndGetCalibratedMaxRpm(FanCurveHelper.MaxCpuRpmKey, telemetry.CpuRpm);
                        _ = FanCurveHelper.ObserveAndGetCalibratedMaxRpm(FanCurveHelper.MaxGpuRpmKey, telemetry.GpuRpm);

                        int effectiveGpuTemp = telemetry.GpuTemp;
                        bool gpuAwakeForTooltip = isAutoApplyActive ? _wasGpuAwake : (effectiveGpuTemp > 0);
                        if (TooltipInputsChanged(telemetry.CpuTemp, effectiveGpuTemp, gpuAwakeForTooltip, _lastTipCpu, _lastTipGpu, _lastTipAwake))
                        {
                            _lastTipCpu = telemetry.CpuTemp;
                            _lastTipGpu = effectiveGpuTemp;
                            _lastTipAwake = gpuAwakeForTooltip;
                            string truncatedTip = FormatTrayTooltip(telemetry.CpuTemp, effectiveGpuTemp, gpuAwakeForTooltip);
                            if (trayIcon.Text != truncatedTip)
                            {
                                trayIcon.Text = truncatedTip;
                            }
                        }

                        if (isCurveActive && telemetry.CpuTemp > 0)
                        {
                            // 3. Load curves from cache or Registry if profile changed or curve updated
                            var activeMode = (PowerProfile)SettingsManager.GetInt("LastPowerMode", (int)PowerProfile.Performance);
                            string pName = SettingsManager.GetString("ActiveFanCurvePreset", "Auto");
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
                            int targetCpuSpeed = FanCurveHelper.GetClampedSpeed(_cachedCpuCurve, telemetry.CpuTemp);
                            int targetGpuSpeed = effectiveGpuTemp == 0
                                ? targetCpuSpeed
                                : FanCurveHelper.GetClampedSpeed(_cachedGpuCurve, effectiveGpuTemp);

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
                                await AcerWmiManager.SetFansAsync(FanProfile.Medium, appliedCpuSpeed, appliedGpuSpeed);
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
