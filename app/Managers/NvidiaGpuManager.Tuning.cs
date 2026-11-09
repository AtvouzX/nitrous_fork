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

    public int SetClocks(int core, int memory) => SetClocksInternal(core, memory) > 0 ? 1 : 0;

    internal int SetClocksInternal(int core, int memory)
    {
        if (!EnsureInitialized()) return 0;

        if (core < MinCoreOffset || core > MaxCoreOffset) return 0;
        if (memory < MinMemoryOffset || memory > MaxMemoryOffset) return 0;

        GetClocks(out int currentCore, out int currentMemory);

        if (Math.Abs(core - currentCore) < 5 && Math.Abs(memory - currentMemory) < 5)
            return 2;

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
            _lastAutoApplyTick = 0;
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

    private PowerProfile? _lastAutoAppliedProfile;
    private int _lastAutoAppliedCore = int.MinValue;
    private int _lastAutoAppliedMem = int.MinValue;
    private long _lastAutoApplyTick;
    public const int AutoApplyCooldownMs = 30000; // 30s cooldown per profile to prevent NvAPI hammering on transient wakes

    private static readonly PowerProfile[] AllProfiles =
        [PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo];

    // Helpers to track default values per profile (opt-in vendor defaults)
    public int GetDefaultCore(PowerProfile profile) => GetVendorDefaults(profile).Core;

    public int GetDefaultMemory(PowerProfile profile) => GetVendorDefaults(profile).Memory;

    private static (int Core, int Memory) GetVendorDefaults(PowerProfile profile)
    {
        if (SettingsManager.GetInt(VendorGpuOcDefaults.ToggleSettingKey, 0) != 1)
        {
            return (0, 0);
        }

        return VendorGpuOcDefaults.GetOffsets(GetNvidiaPciId(), profile);
    }

    /// <summary>
    /// Authorship migration for installs predating <c>GpuSource_{profile}</c>.
    /// Non-zero offsets become Custom; 0/0 is stock, so Default is safe.
    /// </summary>
    private static void MigrateLegacyGpuSources()
    {
        try
        {
            if (SettingsManager.Get("GpuSourceMigrated_v1", 0) == 1) return;

            foreach (var profile in AllProfiles)
            {
                string key = VendorGpuOcDefaults.SourceKey(profile);
                if (SettingsManager.GetString(key, string.Empty).Length > 0) continue;

                int core = SettingsManager.GetInt($"GpuCore_{profile}", 0);
                int memory = SettingsManager.GetInt($"GpuMemory_{profile}", 0);
                var source = VendorGpuOcDefaults.InferLegacySource(core, memory);
                SettingsManager.Save(key, VendorGpuOcDefaults.FormatSource(source));
            }

            SettingsManager.Save("GpuSourceMigrated_v1", 1);
        }
        catch (Exception ex)
        {
            LogManager.Warn($"MigrateLegacyGpuSources failed: {ex.Message}", "NvidiaGpu");
        }
    }

    // Saves the user's custom slider values to the active profile
    public void SaveCustomProfileOc(PowerProfile profile, int core, int memory)
    {
        SettingsManager.Save($"GpuCore_{profile}", ClampCoreOffset(core));
        SettingsManager.Save($"GpuMemory_{profile}", ClampMemoryOffset(memory));
        SettingsManager.Save(
            VendorGpuOcDefaults.SourceKey(profile),
            VendorGpuOcDefaults.FormatSource(VendorGpuOcSource.Custom));
    }

    // Overwrites the custom save with defaults
    public async Task ResetProfileToDefaultsAsync(PowerProfile profile)
    {
        SettingsManager.Save($"GpuCore_{profile}", GetDefaultCore(profile));
        SettingsManager.Save($"GpuMemory_{profile}", GetDefaultMemory(profile));
        // Vendor mode: reset restores the vendor numbers and keeps them vendor-owned.
        // Stock mode: an explicit 0/0 is an authored choice the vendor toggle must respect.
        var source = VendorGpuOcDefaults.ResolveResetSource(
            SettingsManager.GetInt(VendorGpuOcDefaults.ToggleSettingKey, 0) == 1);
        SettingsManager.Save(
            VendorGpuOcDefaults.SourceKey(profile),
            VendorGpuOcDefaults.FormatSource(source));
        _lastAutoApplyTick = 0;
        _lastAutoAppliedProfile = null;
        await ApplyProfileOcAsync(profile, true);
    }

    /// <summary>
    /// Seeds or clears per-profile offsets from the vendor table. Custom profiles
    /// are never modified. Returns true when any profile value changed.
    /// </summary>
    public bool ApplyVendorGpuDefaultsToSettings(bool enable, string? pciId = null)
    {
        SettingsManager.Save(VendorGpuOcDefaults.ToggleSettingKey, enable ? 1 : 0);

        string? identity = pciId ?? GetNvidiaPciId();
        bool changed = false;

        foreach (var profile in AllProfiles)
        {
            var current = VendorGpuOcDefaults.ParseSource(
                SettingsManager.GetString(VendorGpuOcDefaults.SourceKey(profile), string.Empty));

            var (vendorCore, vendorMemory) = VendorGpuOcDefaults.GetOffsets(identity, profile);
            var plan = VendorGpuOcDefaults.ResolveTogglePlan(current, enable, vendorCore, vendorMemory);
            if (plan.Action == VendorGpuOcToggleAction.None) continue;

            SettingsManager.Save($"GpuCore_{profile}", ClampCoreOffset(plan.Core));
            SettingsManager.Save($"GpuMemory_{profile}", ClampMemoryOffset(plan.Memory));
            SettingsManager.Save(
                VendorGpuOcDefaults.SourceKey(profile),
                VendorGpuOcDefaults.FormatSource(plan.ResultSource));
            changed = true;
        }

        if (changed)
        {
            LogManager.Info(
                $"Vendor GPU defaults {(enable ? "enabled" : "disabled")}; per-profile offsets updated.",
                "NvidiaGpu");
        }

        return changed;
    }

    /// <summary>
    /// Toggle entry point: seeds settings, then applies the active profile once
    /// when the dGPU is awake on AC. Otherwise defers to the wake auto-apply path.
    /// </summary>
    public async Task SetUseVendorGpuDefaultsAsync(bool enable)
    {
        bool changed = ApplyVendorGpuDefaultsToSettings(enable);
        if (!changed) return;
        if (!VendorGpuOcDefaults.AllowsImmediateApply(IsGpuAwake(), IsAcPowerConnected())) return;

        _lastAutoApplyTick = 0;
        var active = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        await ApplyProfileOcAsync(active, isAcOnline: true);
    }
}
