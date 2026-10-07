using System;
using System.Reflection;
using System.Threading.Tasks;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class CpuPowerManagerTests
{
    [Theory]
    [InlineData(PowerProfile.Quiet, 85, 0, 80, 0)]
    [InlineData(PowerProfile.Balanced, 99, 0, 95, 0)]
    public void BoostSuppressionProfiles_MustNeverExceed99AndBoostMustBeDisabled(
        PowerProfile profile, int expectedAcMax, int expectedAcBoost, int expectedDcMax, int expectedDcBoost)
    {
        var limits = CpuPowerManager.GetProfileLimits(profile);

        Assert.Equal(expectedAcMax, limits.acMax);
        Assert.Equal(expectedAcBoost, limits.acBoost);
        Assert.Equal(expectedDcMax, limits.dcMax);
        Assert.Equal(expectedDcBoost, limits.dcBoost);

        // Core Invariant checks:
        Assert.True(limits.acMax <= 99, "AC Max state on Quiet/Balanced must be <= 99% to suppress thermal surges");
        Assert.True(limits.dcMax <= 95, "DC Max state on Quiet/Balanced must be <= 95% to preserve battery life");
        Assert.Equal(0, limits.acBoost);
        Assert.Equal(0, limits.dcBoost);
    }

    [Theory]
    [InlineData(PowerProfile.Performance, 100, 2, 99, 0)]
    [InlineData(PowerProfile.Turbo, 100, 2, 99, 0)]
    public void HighPerformanceProfiles_EnableFullClocksOnAC(
        PowerProfile profile, int expectedAcMax, int expectedAcBoost, int expectedDcMax, int expectedDcBoost)
    {
        var limits = CpuPowerManager.GetProfileLimits(profile);

        Assert.Equal(expectedAcMax, limits.acMax);
        Assert.Equal(expectedAcBoost, limits.acBoost);
        Assert.Equal(expectedDcMax, limits.dcMax);
        Assert.Equal(expectedDcBoost, limits.dcBoost);

        // On battery (DC), boost is still suppressed to safeguard battery thermals
        Assert.Equal(0, limits.dcBoost);
    }

    [Fact]
    public void GuidDefinitions_MatchWindowsPowerSubsystemStandards()
    {
        Assert.Equal(new Guid("54533251-82be-4824-96c1-47b60b740d00"), CpuPowerManager.GUID_PROCESSOR_SETTINGS_SUBGROUP);
        Assert.Equal(new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c"), CpuPowerManager.GUID_PROCTHROTTLEMIN);
        Assert.Equal(new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec"), CpuPowerManager.GUID_PROCTHROTTLEMAX);
        Assert.Equal(new Guid("be337238-0d82-4146-a110-4a477b471251"), CpuPowerManager.GUID_PERFBOOSTMODE);
    }

    [Fact]
    public async Task ApplyProfileLimitsAsync_CalledTwice_TriggersCacheGuard()
    {
        // Clear cache via reflection for test isolation
        var cacheFields = new[] { "_lastAcMin", "_lastAcMax", "_lastAcBoost", "_lastDcMin", "_lastDcMax", "_lastDcBoost" };
        foreach (var field in cacheFields)
        {
            var fieldInfo = typeof(CpuPowerManager).GetField(field, BindingFlags.Static | BindingFlags.NonPublic);
            fieldInfo?.SetValue(null, -1);
        }

        // Apply profile once
        await CpuPowerManager.ApplyProfileLimitsAsync(PowerProfile.Balanced);
        
        // Assert cache is updated
        var fieldInfo = typeof(CpuPowerManager).GetField("_lastAcMin", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(fieldInfo);
        var lastAcMin = (int)(fieldInfo.GetValue(null) ?? -1);
        Assert.NotEqual(-1, lastAcMin);

        // Applying it twice should just return quickly due to cache guard.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await CpuPowerManager.ApplyProfileLimitsAsync(PowerProfile.Balanced);
        stopwatch.Stop();
        
        // Ensure it ran extremely fast (cache hit)
        Assert.True(stopwatch.ElapsedMilliseconds < 100, "Second call should return immediately due to cache guard");
    }
}
