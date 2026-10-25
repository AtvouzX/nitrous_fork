using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nitrous.Enums;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;

namespace Nitrous.Managers;

public partial class NvidiaGpuManager
{
    private static readonly FieldInfo? ClockEntryFlagsField =
        typeof(PerformanceStates20ClockEntryV1).GetField(
            "_Flags",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PerformanceStateFlagsField =
        typeof(PerformanceStates20InfoV1.PerformanceState20).GetField(
            "_Flags",
            BindingFlags.Instance | BindingFlags.NonPublic);

    public bool GetClocks(out int core, out int memory)
    {
        core = memory = 0;
        if (!EnsureInitialized()) return false;

        try
        {
            IPerformanceStates20Info states = GPUApi.GetPerformanceStates20(_internalGpu!.Handle);
            var p0Clocks = states.Clocks[PerformanceStateId.P0_3DPerformance];

            var coreClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics);
            var memClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory);

            core = (coreClock?.FrequencyDeltaInkHz.DeltaValue ?? 0) / 1000;
            memory = (memClock?.FrequencyDeltaInkHz.DeltaValue ?? 0) / 1000;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public int SetClocks(int core, int memory) => SetClocksInternal(core, memory);

    private int SetClocksInternal(int core, int memory)
    {
        if (!EnsureInitialized()) return 0;

        if (core < MinCoreOffset || core > MaxCoreOffset) return 0;
        if (memory < MinMemoryOffset || memory > MaxMemoryOffset) return 0;

        GetClocks(out int currentCore, out int currentMemory);

        if (Math.Abs(core - currentCore) < 5 && Math.Abs(memory - currentMemory) < 5)
            return 1;

        try
        {
            // Split core and memory into two independent single-domain calls with the editable flag set.
            // Blackwell-class drivers (GeForce 596.x+) reject combined or non-editable requests with NVAPI_NOT_SUPPORTED.
            if (core != currentCore)
            {
                ApplyClockDomainLocked(_internalGpu!.Handle, PerformanceStateId.P0_3DPerformance, PublicClockDomain.Graphics, core * 1000);
            }

            if (memory != currentMemory)
            {
                ApplyClockDomainLocked(_internalGpu!.Handle, PerformanceStateId.P0_3DPerformance, PublicClockDomain.Memory, memory * 1000);
            }

            LogManager.Info($"Applied GPU offsets: Core={core} MHz, Memory={memory} MHz", "NvidiaGpu");
            return 1;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Single-domain GPU clock apply failed: {ex.Message}. Falling back to combined request.", "NvidiaGpu");
            try
            {
                var coreClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(core * 1000));
                var memoryClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memory * 1000));
                var clocks = new[] { coreClock, memoryClock };
                var performanceState = new PerformanceStates20InfoV1.PerformanceState20(
                    PerformanceStateId.P0_3DPerformance,
                    clocks,
                    Array.Empty<PerformanceStates20BaseVoltageEntryV1>());
                var overclock = new PerformanceStates20InfoV1(new[] { performanceState }, 2, 0);

                GPUApi.SetPerformanceStates20(_internalGpu!.Handle, overclock);
                LogManager.Info($"Applied GPU offsets via combined fallback: Core={core} MHz, Memory={memory} MHz", "NvidiaGpu");
                return 1;
            }
            catch (Exception fallbackEx)
            {
                LogManager.Warn($"Failed to apply GPU offsets: {fallbackEx.Message}", "NvidiaGpu");
                ResetGpuHandle();
                return 0;
            }
        }
    }

    private static PerformanceStates20ClockEntryV1 WithEditableFlag(PerformanceStates20ClockEntryV1 entry)
    {
        try
        {
            if (ClockEntryFlagsField != null)
            {
                object boxed = entry;
                ClockEntryFlagsField.SetValue(boxed, 1u);
                return (PerformanceStates20ClockEntryV1)boxed;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"WithEditableFlag(ClockEntry) reflection exception: {ex.Message}", "NvidiaGpu");
        }
        return entry;
    }

    private static PerformanceStates20InfoV1.PerformanceState20 WithEditableFlag(PerformanceStates20InfoV1.PerformanceState20 state)
    {
        try
        {
            if (PerformanceStateFlagsField != null)
            {
                object boxed = state;
                PerformanceStateFlagsField.SetValue(boxed, 1u);
                return (PerformanceStates20InfoV1.PerformanceState20)boxed;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"WithEditableFlag(PerformanceState) reflection exception: {ex.Message}", "NvidiaGpu");
        }
        return state;
    }

    private static void ApplyClockDomainLocked(
        PhysicalGPUHandle gpuHandle,
        PerformanceStateId stateId,
        PublicClockDomain domain,
        int deltaKhz)
    {
        var entry = new PerformanceStates20ClockEntryV1(
            domain,
            new PerformanceStates20ParameterDelta(deltaKhz));
        entry = WithEditableFlag(entry);

        var performanceState = new PerformanceStates20InfoV1.PerformanceState20(
            stateId,
            new[] { entry },
            Array.Empty<PerformanceStates20BaseVoltageEntryV1>());
        performanceState = WithEditableFlag(performanceState);

        var request = new PerformanceStates20InfoV1(
            new[] { performanceState },
            clocksCount: 1,
            baseVoltagesCount: 0);

        GPUApi.SetPerformanceStates20(gpuHandle, request);
    }

    public async Task<int> AutoApplyClocksAsync(PowerProfile? activeProfile = null, bool? isAcOnline = null)
    {
        bool online = isAcOnline ?? IsAcPowerConnected();
        if (!online || !IsGpuAwake())
        {
            LogManager.Debug("AutoApplyClocksAsync: dGPU is on battery or asleep; skipping auto-apply.", "NvidiaGpu");
            return 0;
        }

        if (SettingsManager.Get("AutoApplyGpuClocks", 1) != 1)
        {
            LogManager.Debug("AutoApplyClocksAsync: AutoApplyGpuClocks disabled in settings.", "NvidiaGpu");
            return 0;
        }

        if (Interlocked.CompareExchange(ref _isAutoApplyingClocks, 1, 0) != 0)
        {
            return 0;
        }

        try
        {
            var profile = activeProfile ?? (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            int core = SettingsManager.Get($"GpuCore_{profile}", GetDefaultCore(profile));
            int mem = SettingsManager.Get($"GpuMemory_{profile}", GetDefaultMemory(profile));

            if (core == 0 && mem == 0) return 0;

            const int maxAttempts = 5;
            const int delayMs = 1000;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (!IsGpuAwake() || !(isAcOnline ?? IsAcPowerConnected())) return 0;

                try
                {
                    if (EnsureInitialized())
                    {
                        int result = SetClocksInternal(core, mem);
                        if (result == 1)
                        {
                            LogManager.Info($"Auto-applied GPU clocks: Core={core} MHz, Memory={mem} MHz (Profile: {profile})", "NvidiaGpu");
                            return 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Debug($"AutoApplyClocksAsync attempt {attempt} failed: {ex.Message}", "NvidiaGpu");
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

    public async Task ApplyOnBootAsync(PowerProfile currentProfile, bool? isAcOnline = null)
    {
        await AutoApplyClocksAsync(currentProfile, isAcOnline);
    }

    public async Task<int> ApplyPowerProfileOcAsync(PowerProfile profile, bool? isAcOnline = null)
    {
        bool online = isAcOnline ?? IsAcPowerConnected();
        if (!online || !IsGpuAwake())
        {
            // Core Battery Invariant: discrete NVIDIA GPU must remain in D3Cold sleep on battery.
            // Never invoke NvAPI when on DC power or when the dGPU is asleep.
            LogManager.Debug($"ApplyPowerProfileOcAsync: dGPU is on battery or asleep; skipping OC for {profile}.", "NvidiaGpu");
            return 0;
        }

        return await Task.Run(() =>
        {
            int coreOffset = SettingsManager.Get($"GpuCore_{profile}", GetDefaultCore(profile));
            int memoryOffset = SettingsManager.Get($"GpuMemory_{profile}", GetDefaultMemory(profile));

            return SetClocksInternal(coreOffset, memoryOffset);
        });
    }

    // Helpers to track default values per profile (opt-in stock defaults)
    public int GetDefaultCore(PowerProfile profile) => 0;

    public int GetDefaultMemory(PowerProfile profile) => 0;

    // Saves the user's custom slider values to the active profile
    public void SaveCustomProfileOc(PowerProfile profile, int core, int memory)
    {
        SettingsManager.Save($"GpuCore_{profile}", core);
        SettingsManager.Save($"GpuMemory_{profile}", memory);
    }

    // Overwrites the custom save with defaults
    public async Task ResetProfileToDefaultsAsync(PowerProfile profile)
    {
        SettingsManager.Save($"GpuCore_{profile}", GetDefaultCore(profile));
        SettingsManager.Save($"GpuMemory_{profile}", GetDefaultMemory(profile));
        await ApplyPowerProfileOcAsync(profile);
    }
}
