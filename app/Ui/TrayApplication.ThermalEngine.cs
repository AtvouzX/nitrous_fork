using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Interop;
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
    private readonly KeyboardBacklightWatchdog _backlightWatchdog = new();
    private int _cpuDownstepHoldTicks;
    private int _gpuDownstepHoldTicks;
    private int _gpuSleepHoldTicks;
    internal int GpuSleepHoldTicks => _gpuSleepHoldTicks;
    public const int HysteresisHoldCycles = 2; // 2 cycles * 2s = 4s delay before stepping down
    private readonly FanRampState _cpuRamp = new();
    private readonly FanRampState _gpuRamp = new();

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

                    // 1b. Evaluate keyboard backlight 30s timeout watchdog.
                    // Off is an armed EC state: Nitrous writes Off while the light is on and the EC
                    // executes it after 30s of real idle, so a pending Off must survive sub-threshold
                    // ticks (UI shows "Off in 30s") instead of being treated as already-off.
                    bool timeoutEnabled = AcerWmiManager.GetBacklightTimeout();
                    var userLevel = AcerWmiManager.GetBacklightLevel() ?? KeyboardBacklightLevel.Level2;
                    bool offPending = AcerWmiManager.IsBacklightOffPending();
                    if (offPending && !timeoutEnabled)
                    {
                        // Timeout unchecked while Off armed/confirmed: cancel and restore previous level.
                        AcerWmiManager.SetBacklightOffPending(false);
                        _ = AcerWmiManager.CancelBacklightOffAsync();
                        _backlightWatchdog.Reset();
                    }
                    else
                    {
                        var idleTime = User32.GetIdleTime();
                        var watchdogAction = _backlightWatchdog.EvaluateTick(timeoutEnabled, userLevel, idleTime, offPending);
                        if (watchdogAction == WatchdogAction.DimToOff)
                        {
                            if (offPending)
                            {
                                // EC has taken the hardware dark: confirm the armed Off.
                                AcerWmiManager.SetBacklightOffPending(false);
                            }
                            _ = AcerWmiManager.ApplyPhysicalBacklightLevelAsync(KeyboardBacklightLevel.Off);
                        }
                        else if (watchdogAction == WatchdogAction.RestoreLevel)
                        {
                            _ = AcerWmiManager.ApplyPhysicalBacklightLevelAsync(userLevel);
                        }
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
                        _cpuRamp.Reset();
                        _gpuRamp.Reset();
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
                                _cpuRamp.Reset();
                                _gpuRamp.Reset();
                            }

                            // 4. Interpolate raw target speeds
                            int targetCpuSpeed = FanCurveHelper.GetClampedSpeed(_cachedCpuCurve, telemetry.CpuTemp);
                            int targetGpuSpeed = effectiveGpuTemp == 0
                                ? targetCpuSpeed
                                : FanCurveHelper.GetClampedSpeed(_cachedGpuCurve, effectiveGpuTemp);

                            // 5. Apply Hysteresis & Ramp-Down:
                            // Ramp-up: Immediate (protects hardware)
                            // Ramp-down: 2-cycle hold, then rate-limited slew
                            string rampSetting = SettingsManager.GetString("FanRampDown", FanRamp.Balanced);
                            int rampRate = FanRamp.PerSecondFor(rampSetting);
                            var tickSpan = TimeSpan.FromSeconds(2);
                            var now = DateTime.UtcNow;

                            int appliedCpuSpeed = _lastAppliedCpuSpeed;
                            if (_lastAppliedCpuSpeed == -1 || targetCpuSpeed >= _lastAppliedCpuSpeed)
                            {
                                appliedCpuSpeed = _cpuRamp.Next(targetCpuSpeed, telemetry.CpuTemp, rampRate, tickSpan, TimeSpan.Zero, now);
                                _cpuDownstepHoldTicks = 0;
                            }
                            else
                            {
                                _cpuDownstepHoldTicks++;
                                if (_cpuDownstepHoldTicks >= HysteresisHoldCycles)
                                {
                                    appliedCpuSpeed = _cpuRamp.Next(targetCpuSpeed, telemetry.CpuTemp, rampRate, tickSpan, TimeSpan.Zero, now);
                                }
                            }

                            int appliedGpuSpeed = _lastAppliedGpuSpeed;
                            if (_lastAppliedGpuSpeed == -1 || targetGpuSpeed >= _lastAppliedGpuSpeed)
                            {
                                appliedGpuSpeed = _gpuRamp.Next(targetGpuSpeed, effectiveGpuTemp, rampRate, tickSpan, TimeSpan.Zero, now);
                                _gpuDownstepHoldTicks = 0;
                            }
                            else
                            {
                                _gpuDownstepHoldTicks++;
                                if (_gpuDownstepHoldTicks >= HysteresisHoldCycles)
                                {
                                    appliedGpuSpeed = _gpuRamp.Next(targetGpuSpeed, effectiveGpuTemp, rampRate, tickSpan, TimeSpan.Zero, now);
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
