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
}
