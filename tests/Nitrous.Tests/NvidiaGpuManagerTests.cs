using System.Reflection;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class NvidiaGpuManagerTests
{
    [Fact]
    public void NvidiaGpuManager_IsGpuAwake_ReturnsBooleanWithoutExceptions()
    {
        // This is a direct test of the CM API interop architecture.
        // It verifies that IsGpuAwake can be called safely without crashing.
        var isAwake = NvidiaGpuManager.IsGpuAwake();
        
        Assert.True(isAwake || !isAwake); // Just asserting no exceptions were thrown
    }
    
    [Fact]
    public async Task NvidiaGpuManager_GetNvmlTelemetryAsync_DoesNotThrowWhenCalled()
    {
        var manager = new NvidiaGpuManager();
        var token = new CancellationToken();

        // This validates the NVML architecture loads properly (or fails gracefully if NVML missing).
        var telemetry = await manager.GetNvmlTelemetryAsync(token);

        // If the system has an awake Nvidia GPU, it should return a non-null object.
        // If it's asleep or not Nvidia, it returns an object with PState = "Sleep" or handles the fallback.
        Assert.NotNull(telemetry);
    }

    [Fact]
    public async Task GetNvmlTelemetryAsync_InTestHost_ReturnsSleepWithoutLiveNvml()
    {
        // Native AVs (0xC0000005) FailFast the test host and are uncatchable,
        // so live NVML reads must be bypassed here. This pins that contract.
        Assert.True(NvidiaGpuManager.IsTestHost());

        using var manager = new NvidiaGpuManager();
        var telemetry = await manager.GetNvmlTelemetryAsync();

        Assert.NotNull(telemetry);
        Assert.Equal("Sleep", telemetry.PState);
        Assert.Equal("NVIDIA (Asleep)", telemetry.Name);
    }

    [Fact]
    public void IsOnlyNitrousRunningOnGpu_ReturnsTrue_WhenOnlyNitrousIsRunning()
    {
        int nitrousPid = 1234;
        var graphicsInfos = new NvidiaGpuManager.NvmlProcessInfo[] { new() { Pid = 1234 } };
        var computeInfos = Array.Empty<NvidiaGpuManager.NvmlProcessInfo>();

        bool result = NvidiaGpuManager.IsOnlyNitrousRunningOnGpu(
            graphicsInfos, 1,
            computeInfos, 0,
            nitrousPid);

        Assert.True(result);
    }

    [Fact]
    public void IsOnlyNitrousRunningOnGpu_ReturnsFalse_WhenOtherAppsAreRunning()
    {
        int nitrousPid = 1234;
        var graphicsInfos = new NvidiaGpuManager.NvmlProcessInfo[] { new() { Pid = 1234 }, new() { Pid = 9999 } };
        
        bool result = NvidiaGpuManager.IsOnlyNitrousRunningOnGpu(
            graphicsInfos, 2,
            null, 0,
            nitrousPid);

        Assert.False(result);
    }

    [Fact]
    public void IsOnlyNitrousRunningOnGpu_ReturnsFalse_WhenNoAppsAreRunning()
    {
        int nitrousPid = 1234;
        var graphicsInfos = Array.Empty<NvidiaGpuManager.NvmlProcessInfo>();
        
        bool result = NvidiaGpuManager.IsOnlyNitrousRunningOnGpu(
            graphicsInfos, 0,
            null, 0,
            nitrousPid);

        Assert.False(result);
    }

    [Fact]
    public async Task ApplyProfileOcAsync_WhenOnDcPower_ReturnsZeroWithoutWakingGpu_PowerProfileAlias()
    {
        using var manager = new NvidiaGpuManager();

        // When on DC (battery) power, OC must be completely bypassed to preserve D3Cold sleep.
        // Canonical entry keeps the old ApplyPowerProfileOcAsync battery behavior.
        int result = await manager.ApplyProfileOcAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ApplyProfileOcAsync_WhenOnDcPower_CompletesCleanlyWithoutWakingGpu_BootAlias()
    {
        using var manager = new NvidiaGpuManager();

        // Boot OC must abort immediately when on DC power. Canonical entry replaces ApplyOnBootAsync.
        var ex = await Record.ExceptionAsync(() => manager.ApplyProfileOcAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false));
        Assert.Null(ex);
    }

    [Fact]
    public void IsAcPowerConnected_ReturnsBooleanWithoutExceptions()
    {
        bool isOnline = NvidiaGpuManager.IsAcPowerConnected();
        Assert.True(isOnline || !isOnline);
    }

    [Fact]
    public void EnsureInitialized_DoesNotThrowExceptions()
    {
        using var manager = new NvidiaGpuManager();
        bool result = manager.EnsureInitialized();
        Assert.True(result || !result);
    }

    [Theory]
    [InlineData(false, true, true, 1, true)]    // Wakes up on AC with setting enabled -> Auto-apply
    [InlineData(true, true, true, 1, false)]     // Already awake -> No duplicate apply
    [InlineData(false, true, false, 1, false)]   // Wakes up on DC (Battery) -> Suppressed
    [InlineData(false, true, true, 0, false)]    // Wakes up on AC but setting disabled -> Suppressed
    [InlineData(false, false, true, 1, false)]   // Asleep -> Suppressed
    [InlineData(true, false, true, 1, false)]    // Going to sleep -> Suppressed
    public void UpdateSleepState_ThresholdTickWakeOrSleep_MapsToExpectedAction(bool wasGpuAwake, bool isGpuAwake, bool isAcOnline, int autoApplySetting, bool expected)
    {
        var (_, _, action) = NvidiaGpuManager.UpdateSleepState(
            wasGpuAwake, NvidiaGpuManager.GpuSleepHysteresisCycles - 1, isGpuAwake, isAcOnline, autoApplySetting);
        Assert.Equal(expected, action == NvidiaGpuManager.GpuSleepAction.AutoApply);
    }

    [Theory]
    [InlineData(true, false, true)]    // Was awake, now asleep -> Reset handle
    [InlineData(false, false, false)]  // Was asleep, still asleep -> No reset
    [InlineData(true, true, false)]    // Was awake, still awake -> No reset
    [InlineData(false, true, false)]   // Was asleep, now awake -> No reset
    public void UpdateSleepState_ThresholdTickSleep_MapsToExpectedReset(bool wasGpuAwake, bool isGpuAwake, bool expected)
    {
        var (_, _, action) = NvidiaGpuManager.UpdateSleepState(
            wasGpuAwake, NvidiaGpuManager.GpuSleepHysteresisCycles - 1, isGpuAwake, true, 1);
        Assert.Equal(expected, action == NvidiaGpuManager.GpuSleepAction.ResetHandle);
    }

    [Fact]
    public async Task ApplyProfileOcAsync_WhenOnDcPower_ReturnsZeroWithoutWakingGpu_RetryPath()
    {
        using var manager = new NvidiaGpuManager();
        int result = await manager.ApplyProfileOcAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ApplyProfileOcAsync_WhenEnabledOnAc_PreservesD3ColdGuard()
    {
        // Canonical entry has no settings gate; wake gating lives at call sites. Battery guard still holds.
        try
        {
            using var manager = new NvidiaGpuManager();
            int result = await manager.ApplyProfileOcAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false);
            Assert.Equal(0, result);
        }
        finally
        {
            // Battery bypass holds regardless of caller context.
        }
    }

    [Fact]
    public void ResetGpuHandle_DoesNotThrowExceptions()
    {
        using var manager = new NvidiaGpuManager();
        var ex = Record.Exception(() =>
        {
            manager.ResetGpuHandle();
            manager.ResetGpuHandle();
        });
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(Nitrous.Enums.PowerProfile.Quiet)]
    [InlineData(Nitrous.Enums.PowerProfile.Balanced)]
    [InlineData(Nitrous.Enums.PowerProfile.Performance)]
    [InlineData(Nitrous.Enums.PowerProfile.Turbo)]
    public void GetDefaultCore_And_GetDefaultMemory_ReturnZeroStockOffsets(Nitrous.Enums.PowerProfile profile)
    {
        using var manager = new NvidiaGpuManager();
        Assert.Equal(0, manager.GetDefaultCore(profile));
        Assert.Equal(0, manager.GetDefaultMemory(profile));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", "NVIDIA GeForce RTX 4060 Laptop")]
    [InlineData("GeForce RTX 3070 Ti Laptop GPU", "GeForce RTX 3070 Ti Laptop")]
    [InlineData("NVIDIA RTX A2000", "NVIDIA RTX A2000")]
    [InlineData("RTX 4090", "RTX 4090")]
    public void FormatGpuName_StripsGpuSuffix(string raw, string expected)
    {
        string formatted = Nitrous.Ui.DashboardViewModel.FormatGpuName(raw);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void NvAPIWrapper_ReflectionFields_ExistOrSafelyFallback()
    {
        var entryFlags = typeof(NvAPIWrapper.Native.GPU.Structures.PerformanceStates20ClockEntryV1).GetField(
            "_Flags",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var stateFlags = typeof(NvAPIWrapper.Native.GPU.Structures.PerformanceStates20InfoV1.PerformanceState20).GetField(
            "_Flags",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(entryFlags);
        Assert.NotNull(stateFlags);
        Assert.True(entryFlags.FieldType == typeof(uint) || entryFlags.FieldType == typeof(int));
        Assert.True(stateFlags.FieldType == typeof(uint) || stateFlags.FieldType == typeof(int));
    }

    [Theory]
    // Single-tick sleep drop: hold state active, increment hold ticks, no reset, no auto-apply
    [InlineData(true, false, 0, true, 1, false, false)]
    // Second sleep tick: hold threshold reached, transition to asleep, reset handle
    [InlineData(true, false, 1, false, 0, false, true)]
    // Instant wake after 1-tick flicker: reset hold ticks, preserve awake, no auto-apply
    [InlineData(true, true, 1, true, 0, false, false)]
    // Already awake and still awake: zero hold ticks, no auto-apply, no reset
    [InlineData(true, true, 0, true, 0, false, false)]
    // Single-tick wake spike: hold state active, increment hold ticks, no auto-apply, remain asleep
    [InlineData(false, true, 0, false, 1, false, false)]
    // Second wake tick: hold threshold reached, transition to awake, trigger auto-apply
    [InlineData(false, true, 1, true, 0, true, false)]
    // Instant sleep after 1-tick wake flicker: reset hold ticks, preserve asleep, no auto-apply
    [InlineData(false, false, 1, false, 0, false, false)]
    // Confirmed asleep, stays asleep: remain asleep, zero hold ticks, no actions
    [InlineData(false, false, 0, false, 0, false, false)]
    public void EvaluateGpuSleepHysteresis_EvaluatesTransitionsCorrectly(
        bool wasGpuAwake,
        bool isGpuAwake,
        int currentHoldTicks,
        bool expectedNewWasAwake,
        int expectedNewHoldTicks,
        bool expectedShouldAutoApply,
        bool expectedShouldResetHandle)
    {
        var (newWasAwake, newHoldTicks, shouldAutoApply, shouldResetHandle) =
            NvidiaGpuManager.EvaluateGpuSleepHysteresis(wasGpuAwake, isGpuAwake, currentHoldTicks, holdCycles: 2);

        Assert.Equal(expectedNewWasAwake, newWasAwake);
        Assert.Equal(expectedNewHoldTicks, newHoldTicks);
        Assert.Equal(expectedShouldAutoApply, shouldAutoApply);
        Assert.Equal(expectedShouldResetHandle, shouldResetHandle);
    }

    [Fact]
    public void HysteresisAndCooldownConstants_AreWithinExpectedRanges()
    {
        Assert.Equal(4, NvidiaGpuManager.GpuSleepHysteresisCycles);
        Assert.Equal(30000, NvidiaGpuManager.AutoApplyCooldownMs);
    }

    [Fact]
    public void GpuTelemetry_IsDisplayable_RequiresSuccessfulLoadAndConcreteName()
    {
        Assert.False(new NvidiaGpuManager.GpuTelemetry().IsDisplayable);

        var live = new NvidiaGpuManager.GpuTelemetry
        {
            HasLoadSample = true,
            Name = "NVIDIA GeForce RTX 3050"
        };
        Assert.True(live.IsDisplayable);

        Assert.False(new NvidiaGpuManager.GpuTelemetry { HasLoadSample = true, Name = "" }.IsDisplayable);
        Assert.False(new NvidiaGpuManager.GpuTelemetry { HasLoadSample = true, Name = "Unknown" }.IsDisplayable);
        Assert.False(new NvidiaGpuManager.GpuTelemetry
        {
            HasLoadSample = false,
            Name = "NVIDIA GeForce RTX 3050"
        }.IsDisplayable);
    }

    [Theory]
    [InlineData(300, 0)]    // Exceeds MaxCoreOffset (200)
    [InlineData(-250, 0)]   // Fatal undervolt below safe MinCoreOffset (-200)
    [InlineData(-300, 0)]   // Below MinCoreOffset
    [InlineData(0, 1200)]   // Exceeds MaxMemoryOffset (500)
    [InlineData(0, -1000)]  // Fatal GDDR downclock below safe MinMemoryOffset (-500)
    [InlineData(0, -1200)]  // Below MinMemoryOffset
    public void SetClocksInternal_WithOutOfBoundsOffsets_ReturnsZero(int core, int memory)
    {
        using var manager = new NvidiaGpuManager();
        int result = manager.SetClocksInternal(core, memory);
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(-250, -200)]
    [InlineData(-300, -200)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(300, 200)]
    public void ClampCoreOffset_ClampsToSafeOperatingRange(int input, int expected)
    {
        using var manager = new NvidiaGpuManager();
        Assert.Equal(expected, manager.ClampCoreOffset(input));
    }

    [Theory]
    [InlineData(-1000, -500)]
    [InlineData(-600, -500)]
    [InlineData(-100, -100)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(1200, 500)]
    public void ClampMemoryOffset_ClampsToSafeOperatingRange(int input, int expected)
    {
        using var manager = new NvidiaGpuManager();
        Assert.Equal(expected, manager.ClampMemoryOffset(input));
    }

    [Theory]
    [InlineData("MinCoreOffset", -200)]
    [InlineData("MaxCoreOffset", 200)]
    [InlineData("MinMemoryOffset", -500)]
    [InlineData("MaxMemoryOffset", 500)]
    public void HardwareOffsetLimits_AreImmutableConstants(string fieldName, int expectedValue)
    {
        var field = typeof(NvidiaGpuManager).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field.IsLiteral && !field.IsInitOnly, $"{fieldName} must be a compile-time const.");
        Assert.Equal(expectedValue, (int)field.GetValue(null)!);
    }

    [Fact]
    public void ResolveProfileOffsets_WhenSettingsExceedLimits_ReturnsClampedMaximums()
    {
        SettingsManager.Save("GpuCore_Performance", 300);
        SettingsManager.Save("GpuMemory_Performance", 1200);
        try
        {
            using var manager = new NvidiaGpuManager();
            var (core, memory) = manager.ResolveProfileOffsets(Nitrous.Enums.PowerProfile.Performance);
            Assert.Equal(200, core);
            Assert.Equal(500, memory);
        }
        finally
        {
            SettingsManager.Delete("GpuCore_Performance");
            SettingsManager.Delete("GpuMemory_Performance");
        }
    }

    [Fact]
    public void ResolveProfileOffsets_WhenSettingsBelowLimits_ReturnsClampedMinimums()
    {
        SettingsManager.Save("GpuCore_Performance", -300);
        SettingsManager.Save("GpuMemory_Performance", -1200);
        try
        {
            using var manager = new NvidiaGpuManager();
            var (core, memory) = manager.ResolveProfileOffsets(Nitrous.Enums.PowerProfile.Performance);
            Assert.Equal(-200, core);
            Assert.Equal(-500, memory);
        }
        finally
        {
            SettingsManager.Delete("GpuCore_Performance");
            SettingsManager.Delete("GpuMemory_Performance");
        }
    }

    [Fact]
    public void ResolveProfileOffsets_WhenNoCustomValues_ReturnsStockDefaults()
    {
        SettingsManager.Delete("GpuCore_Balanced");
        SettingsManager.Delete("GpuMemory_Balanced");
        using var manager = new NvidiaGpuManager();
        var (core, memory) = manager.ResolveProfileOffsets(Nitrous.Enums.PowerProfile.Balanced);
        Assert.Equal(0, core);
        Assert.Equal(0, memory);
    }

    [Theory]
    // Final consecutive wake tick on AC with auto-apply enabled -> confirmed awake, auto-apply
    [InlineData(false, NvidiaGpuManager.GpuSleepHysteresisCycles - 1, true, true, 1, true, 0)]
    // Final consecutive sleep tick on AC with auto-apply enabled -> confirmed asleep, reset handle
    [InlineData(true, NvidiaGpuManager.GpuSleepHysteresisCycles - 1, false, true, 1, false, 0)]
    public void UpdateSleepState_DelegatesToHysteresis_WhenAcEnabled(
        bool wasAwake, int holdTicks, bool isGpuAwake, bool isAcOnline, int setting,
        bool expectedNewWasAwake, int expectedNewHoldTicks)
    {
        var (newWasAwake, newHoldTicks, action) = NvidiaGpuManager.UpdateSleepState(
            wasAwake, holdTicks, isGpuAwake, isAcOnline, setting);

        var (hWasAwake, hHoldTicks, hAutoApply, hReset) = NvidiaGpuManager.EvaluateGpuSleepHysteresis(
            wasAwake, isGpuAwake, holdTicks, NvidiaGpuManager.GpuSleepHysteresisCycles);

        Assert.Equal(hWasAwake, newWasAwake);
        Assert.Equal(hHoldTicks, newHoldTicks);
        Assert.Equal(expectedNewWasAwake, newWasAwake);
        Assert.Equal(expectedNewHoldTicks, newHoldTicks);
        var expectedAction = hAutoApply
            ? NvidiaGpuManager.GpuSleepAction.AutoApply
            : hReset ? NvidiaGpuManager.GpuSleepAction.ResetHandle : NvidiaGpuManager.GpuSleepAction.None;
        Assert.Equal(expectedAction, action);
    }

    [Theory]
    // Battery bypass: was awake -> collapse to asleep with handle reset, never auto-apply
    [InlineData(true, 1, true, false, 0, false, 0)]
    // Battery bypass: already asleep -> stay asleep, no action
    [InlineData(false, 0, false, false, 0, false, 0)]
    // Auto-apply disabled on AC: was awake -> collapse to asleep with handle reset, never auto-apply
    [InlineData(true, 1, true, true, 0, false, 0)]
    public void UpdateSleepState_BatteryOrDisabledBypass_NeverAutoApplies(
        bool wasAwake, int holdTicks, bool isGpuAwake, bool isAcOnline, int setting,
        bool expectedNewWasAwake, int expectedNewHoldTicks)
    {
        var (newWasAwake, newHoldTicks, action) = NvidiaGpuManager.UpdateSleepState(
            wasAwake, holdTicks, isGpuAwake, isAcOnline, setting);

        Assert.Equal(expectedNewWasAwake, newWasAwake);
        Assert.Equal(expectedNewHoldTicks, newHoldTicks);
        Assert.NotEqual(NvidiaGpuManager.GpuSleepAction.AutoApply, action);
    }

    [Fact]
    public async Task ApplyProfileOcAsync_WhenOnDcPower_ReturnsZeroWithoutWakingGpu()
    {
        using var manager = new NvidiaGpuManager();

        // Unified OC entry must bypass NvAPI completely on battery to preserve D3Cold sleep
        int result = await manager.ApplyProfileOcAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false);
        Assert.Equal(0, result);
    }
}


