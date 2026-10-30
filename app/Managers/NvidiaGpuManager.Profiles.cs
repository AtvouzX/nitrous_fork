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
    /// Single resolver for per-profile clock offsets: reads stored values with
    /// stock defaults, then clamps to safe operating ranges.
    /// Consolidates the Settings.Get + GetDefault + Clamp triad previously
    /// repeated at each call site.
    /// </summary>
    public (int Core, int Memory) ResolveProfileOffsets(PowerProfile profile)
    {
        int rawCore = SettingsManager.GetInt($"GpuCore_{profile}", GetDefaultCore(profile));
        int rawMemory = SettingsManager.GetInt($"GpuMemory_{profile}", GetDefaultMemory(profile));
        return (ClampCoreOffset(rawCore), ClampMemoryOffset(rawMemory));
    }

    /// <summary>
    /// Pure sleep-state transition evaluator. On battery or when auto-apply is
    /// disabled, collapses to asleep (resetting the handle if previously awake)
    /// without ever auto-applying, preserving D3Cold. Otherwise delegates to
    /// EvaluateGpuSleepHysteresis and maps its flags to a single action.
    /// Takes the wake reading as a parameter; never queries hardware itself.
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
    /// applies them. Battery/asleep guard preserves D3Cold (never touches NvAPI).
    /// Existing ApplyOnBoot/AutoApply/ApplyPowerProfileOc/SetClocks paths are
    /// unchanged; this is the canonical path for new callers.
    /// </summary>
    public async Task<int> ApplyProfileOcAsync(PowerProfile profile, bool? isAcOnline = null)
    {
        bool online = isAcOnline ?? IsAcPowerConnected();
        if (!online || !IsGpuAwake())
        {
            LogManager.Debug($"ApplyProfileOcAsync: dGPU is on battery or asleep; skipping OC for {profile}.", "NvidiaGpu");
            return 0;
        }

        return await Task.Run(() =>
        {
            var (core, memory) = ResolveProfileOffsets(profile);
            return SetClocksInternal(core, memory);
        });
    }
}
