using Nitrous.Enums;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class DialGeometryHelperTests
{
    [Theory]
    [InlineData(PowerProfile.Quiet, 180.0)]
    [InlineData(PowerProfile.Balanced, 270.0)]
    [InlineData(PowerProfile.Performance, 360.0)]
    [InlineData(PowerProfile.Turbo, 45.0)]
    [InlineData((PowerProfile)999, 270.0)]
    public void GetNeedleAngle_ReturnsExpectedAngleForProfile(PowerProfile profile, double expectedAngle)
    {
        double angle = DialGeometryHelper.GetNeedleAngle(profile);
        Assert.Equal(expectedAngle, angle);
    }

    [Theory]
    [InlineData(0, 0, 100, 0.0, 100, 0)]
    [InlineData(0, 0, 100, 90.0, 0, 100)]
    [InlineData(0, 0, 100, 180.0, -100, 0)]
    [InlineData(0, 0, 100, 270.0, 0, -100)]
    [InlineData(0, 0, 100, 360.0, 100, 0)]
    [InlineData(50, 50, 10, 180.0, 40, 50)]
    [InlineData(50, 50, 10, 270.0, 50, 40)]
    [InlineData(50, 50, 10, 360.0, 60, 50)]
    public void PolarToCartesian_ComputesAccurate2DCoordinates(
        int centerX, int centerY, double radius, double angleDegrees, int expectedX, int expectedY)
    {
        var (x, y) = DialGeometryHelper.PolarToCartesian(centerX, centerY, radius, angleDegrees);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void CalculateNeedleTip_ComputesExpectedTipCoordinates()
    {
        int cx = 100;
        int cy = 100;
        double radius = 50.0;

        // Quiet: 180 deg -> (-50, 0) relative to center -> (50, 100)
        var quietTip = DialGeometryHelper.CalculateNeedleTip(cx, cy, radius, PowerProfile.Quiet);
        Assert.Equal((50, 100), quietTip);

        // Balanced: 270 deg -> (0, -50) relative to center -> (100, 50)
        var balancedTip = DialGeometryHelper.CalculateNeedleTip(cx, cy, radius, PowerProfile.Balanced);
        Assert.Equal((100, 50), balancedTip);

        // Performance: 360 deg -> (+50, 0) relative to center -> (150, 100)
        var perfTip = DialGeometryHelper.CalculateNeedleTip(cx, cy, radius, PowerProfile.Performance);
        Assert.Equal((150, 100), perfTip);

        // Turbo: 45 deg -> dx = Round(50 * Cos(45°)) = 35, dy = Round(50 * Sin(45°)) = 35 -> (135, 135)
        var turboTip = DialGeometryHelper.CalculateNeedleTip(cx, cy, radius, PowerProfile.Turbo);
        Assert.Equal((135, 135), turboTip);
    }

    [Fact]
    public void ArcConstants_MatchPhysicalGaugeStandards()
    {
        Assert.Equal(135.0, DialGeometryHelper.ArcStartAngle);
        Assert.Equal(270.0, DialGeometryHelper.ArcSweepAngle);
    }
}

