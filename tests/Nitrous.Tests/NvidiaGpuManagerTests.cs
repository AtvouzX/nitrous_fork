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
    public async Task ApplyPowerProfileOcAsync_WhenOnDcPower_ReturnsZeroWithoutWakingGpu()
    {
        using var manager = new NvidiaGpuManager();
        
        // When on DC (battery) power, OC must be completely bypassed to preserve D3Cold sleep
        int result = await manager.ApplyPowerProfileOcAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ApplyOnBootAsync_WhenOnDcPower_CompletesCleanlyWithoutWakingGpu()
    {
        using var manager = new NvidiaGpuManager();

        // Boot OC must abort immediately when on DC power
        var ex = await Record.ExceptionAsync(() => manager.ApplyOnBootAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false));
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
    public void ShouldAutoApplyGpuClocks_EvaluatesCorrectly(bool wasGpuAwake, bool isGpuAwake, bool isAcOnline, int autoApplySetting, bool expected)
    {
        bool result = NvidiaGpuManager.ShouldAutoApplyGpuClocks(wasGpuAwake, isGpuAwake, isAcOnline, autoApplySetting);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false, true)]    // Was awake, now asleep -> Reset handle
    [InlineData(false, false, false)]  // Was asleep, still asleep -> No reset
    [InlineData(true, true, false)]    // Was awake, still awake -> No reset
    [InlineData(false, true, false)]   // Was asleep, now awake -> No reset
    public void ShouldResetGpuHandle_EvaluatesCorrectly(bool wasGpuAwake, bool isGpuAwake, bool expected)
    {
        bool result = NvidiaGpuManager.ShouldResetGpuHandle(wasGpuAwake, isGpuAwake);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task AutoApplyClocksAsync_WhenOnDcPower_ReturnsZeroWithoutWakingGpu()
    {
        using var manager = new NvidiaGpuManager();
        int result = await manager.AutoApplyClocksAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: false);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task AutoApplyClocksAsync_WhenDisabledInSettings_ReturnsZero()
    {
        SettingsManager.Save("AutoApplyGpuClocks", 0);
        try
        {
            using var manager = new NvidiaGpuManager();
            int result = await manager.AutoApplyClocksAsync(Nitrous.Enums.PowerProfile.Performance, isAcOnline: true);
            Assert.Equal(0, result);
        }
        finally
        {
            SettingsManager.Save("AutoApplyGpuClocks", 1);
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
    // Genuine wake from confirmed sleep: transition to awake, trigger auto-apply
    [InlineData(false, true, 0, true, 0, true, false)]
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
        Assert.Equal(2, NvidiaGpuManager.GpuSleepHysteresisCycles);
        Assert.Equal(30000, NvidiaGpuManager.AutoApplyCooldownMs);
    }

    [Theory]
    [InlineData(300, 0)]    // Exceeds MaxCoreOffset (250)
    [InlineData(-300, 0)]   // Below MinCoreOffset (-250)
    [InlineData(0, 1200)]   // Exceeds MaxMemoryOffset (1000)
    [InlineData(0, -1200)]  // Below MinMemoryOffset (-1000)
    public void SetClocksInternal_WithOutOfBoundsOffsets_ReturnsZero(int core, int memory)
    {
        using var manager = new NvidiaGpuManager();
        int result = manager.SetClocksInternal(core, memory);
        Assert.Equal(0, result);
    }
}


