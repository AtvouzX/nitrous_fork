using System;
using System.Threading;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public partial class NvidiaGpuManager
{
    public enum GpuSleepAction
    {
        None,
        AutoApply,
        ResetHandle
    }

    /// <summary>
    /// Single resolver for per-profile clock offsets: reads stored values
    /// stock defaults, then clamps safe operating ranges.
    /// Consolidates Settings.Get + GetDefault + Clamp triad previously
    /// repeated at call site.
    /// </summary>
    public (int Core, int Memory) ResolveProfileOffsets(PowerProfile profile)
    {
        int rawCore = SettingsManager.GetInt($"GpuCore_{profile}", GetDefaultCore(profile));
        int rawMemory = SettingsManager.GetInt($"GpuMemory_{profile}", GetDefaultMemory(profile));
        return (ClampCoreOffset(rawCore), ClampMemoryOffset(rawMemory));
    }

    /// <summary>
    /// Pure sleep-state transition evaluator. On battery or auto-apply
    /// disabled, collapses asleep (resetting handle previously awake)
    /// without ever auto-applying, preserving D3Cold. Otherwise delegates
    /// EvaluateGpuSleepHysteresis and maps flags single action.
    /// Takes wake reading parameter; never queries hardware itself.
    /// </summary>
    public static (bool NewWasAwake, int NewHoldTicks, GpuSleepAction Action) UpdateSleepState(
        bool wasAwake,
        int holdTicks,
        bool isGpuAwake,
        bool isAcOnline,
        int autoApplySetting)
    {
        if (!isAcOnline || autoApplySetting != 1)
        {
            return (false, 0, wasAwake ? GpuSleepAction.ResetHandle : GpuSleepAction.None);
        }

        var (newWasAwake, newHoldTicks, shouldAutoApply, shouldResetHandle) =
            EvaluateGpuSleepHysteresis(wasAwake, isGpuAwake, holdTicks, GpuSleepHysteresisCycles);

        var action = shouldAutoApply
            ? GpuSleepAction.AutoApply
            : shouldResetHandle ? GpuSleepAction.ResetHandle : GpuSleepAction.None;
        return (newWasAwake, newHoldTicks, action);
    }

    /// <summary>
    /// Unified single-shot OC entry: resolves clamped profile offsets and
    /// applies them with interlock, per-profile cooldown, and NvAPI retry.
    /// Battery/asleep guard preserves D3Cold (never touches NvAPI).
    /// Explicit callers only; wake auto-apply gating (AutoApplyGpuClocks
    /// setting) lives at call sites, not here.
    /// </summary>
    public async Task<int> ApplyProfileOcAsync(PowerProfile profile, bool? isAcOnline = null)
    {
        SetWhisperMode(IsWhisperProfile(profile));

        bool online = isAcOnline ?? IsAcPowerConnected();
        if (!online || !IsGpuAwake())
        {
            LogManager.Debug($"ApplyProfileOcAsync: dGPU is on battery or asleep; skipping OC for {profile}.", "NvidiaGpu");
            return 0;
        }

        if (Interlocked.CompareExchange(ref _isAutoApplyingClocks, 1, 0) != 0)
        {
            return 0;
        }

        try
        {
            var (core, memory) = ResolveProfileOffsets(profile);

            long now = Environment.TickCount64;
            if (profile == _lastAutoAppliedProfile &&
                core == _lastAutoAppliedCore &&
                memory == _lastAutoAppliedMem &&
                (now - _lastAutoApplyTick) < AutoApplyCooldownMs)
            {
                LogManager.Debug($"ApplyProfileOcAsync: Clocks were recently applied for {profile}; cooldown active.", "NvidiaGpu");
                return 1;
            }

            const int maxAttempts = 5;
            const int delayMs = 1000;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (!IsGpuAwake() || !(isAcOnline ?? IsAcPowerConnected())) return 0;

                try
                {
                    if (EnsureInitialized())
                    {
                        int result = SetClocksInternal(core, memory);
                        if (result == 1)
                        {
                            _lastAutoAppliedProfile = profile;
                            _lastAutoAppliedCore = core;
                            _lastAutoAppliedMem = memory;
                            _lastAutoApplyTick = Environment.TickCount64;
                            LogManager.Info($"Auto-applied GPU clocks: Core={core} MHz, Memory={memory} MHz (Profile: {profile})", "NvidiaGpu");
                            return 1;
                        }
                        if (result == 2)
                        {
                            _lastAutoAppliedProfile = profile;
                            _lastAutoAppliedCore = core;
                            _lastAutoAppliedMem = memory;
                            _lastAutoApplyTick = Environment.TickCount64;
                            LogManager.Debug($"ApplyProfileOcAsync: GPU clocks already in sync: Core={core} MHz, Memory={memory} MHz (Profile: {profile})", "NvidiaGpu");
                            return 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Debug($"ApplyProfileOcAsync attempt {attempt} failed: {ex.Message}", "NvidiaGpu");
                }

                if (attempt < maxAttempts)
                {
                    ResetGpuHandle();
                    await Task.Delay(delayMs);
                }
            }

            return 0;
        }
        finally
        {
            Interlocked.Exchange(ref _isAutoApplyingClocks, 0);
        }
    }
}
