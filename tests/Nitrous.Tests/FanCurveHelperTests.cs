using System.Windows;
using Nitrous.Enums;
using Nitrous.Helpers;
using Xunit;
using Point = System.Windows.Point;

namespace Nitrous.Tests;

public class FanCurveHelperTests
{
    [Fact]
    public void InterpolateSpeed_NullCurve_ReturnsDefault50()
    {
        int speed = FanCurveHelper.InterpolateSpeed(null, 50);
        Assert.Equal(50, speed);
    }

    [Fact]
    public void InterpolateSpeed_EmptyCurve_ReturnsDefault50()
    {
        int speed = FanCurveHelper.InterpolateSpeed(new List<Point>(), 50);
        Assert.Equal(50, speed);
    }

    [Theory]
    [InlineData(10, 20)] // Far below lowest waypoint (30°C) -> clamps to 20%
    [InlineData(29, 20)]
    [InlineData(30, 20)] // Exact match at lowest waypoint
    public void InterpolateSpeed_BelowOrEqualToLowestWaypoint_ClampsToLowestSpeed(int temp, int expectedSpeed)
    {
        var curve = new List<Point>
        {
            new(30, 20),
            new(50, 40),
            new(70, 60),
            new(90, 80)
        };

        int speed = FanCurveHelper.InterpolateSpeed(curve, temp);
        Assert.Equal(expectedSpeed, speed);
    }

    [Theory]
    [InlineData(90, 80)]  // Exact match at highest waypoint
    [InlineData(95, 80)]  // Above highest waypoint -> clamps to 80%
    [InlineData(110, 80)] // Extreme temperature -> clamps to 80%
    public void InterpolateSpeed_AboveOrEqualToHighestWaypoint_ClampsToHighestSpeed(int temp, int expectedSpeed)
    {
        var curve = new List<Point>
        {
            new(30, 20),
            new(50, 40),
            new(70, 60),
            new(90, 80)
        };

        int speed = FanCurveHelper.InterpolateSpeed(curve, temp);
        Assert.Equal(expectedSpeed, speed);
    }

    [Theory]
    [InlineData(40, 30)] // Midpoint between (30, 20) and (50, 40) -> 30%
    [InlineData(60, 50)] // Midpoint between (50, 40) and (70, 60) -> 50%
    [InlineData(80, 70)] // Midpoint between (70, 60) and (90, 80) -> 70%
    public void InterpolateSpeed_BetweenWaypoints_LinearlyInterpolates(int temp, int expectedSpeed)
    {
        var curve = new List<Point>
        {
            new(30, 20),
            new(50, 40),
            new(70, 60),
            new(90, 80)
        };

        int speed = FanCurveHelper.InterpolateSpeed(curve, temp);
        Assert.Equal(expectedSpeed, speed);
    }

    [Fact]
    public void InterpolateSpeed_ZeroSpanWaypoints_HandlesWithoutDivisionByZero()
    {
        // Edge case: Two points with identical temperatures
        var curve = new List<Point>
        {
            new(50, 30),
            new(50, 70),
            new(80, 90)
        };

        // Must not throw DivideByZeroException
        int speed = FanCurveHelper.InterpolateSpeed(curve, 50);
        Assert.True(speed >= 0 && speed <= 100);
    }

    [Theory]
    [InlineData(PowerProfile.Quiet)]
    [InlineData(PowerProfile.Balanced)]
    [InlineData(PowerProfile.Performance)]
    [InlineData(PowerProfile.Turbo)]
    public void GetDefaultCpuCurve_ReturnsValidMonotonicCurve(PowerProfile profile)
    {
        var curve = FanCurveHelper.GetDefaultCpuCurve(profile);

        Assert.NotNull(curve);
        Assert.True(curve.Count >= 4, "Curve must have at least 4 waypoints");

        for (int i = 0; i < curve.Count - 1; i++)
        {
            Assert.True(curve[i].X < curve[i + 1].X, $"Temperatures must strictly increase: {curve[i].X} vs {curve[i + 1].X}");
            Assert.True(curve[i].Y <= curve[i + 1].Y, $"Speeds must monotonically increase: {curve[i].Y} vs {curve[i + 1].Y}");
            Assert.True(curve[i].Y >= 0 && curve[i].Y <= 100, $"Speed must be in [0, 100]: {curve[i].Y}");
        }
    }

    [Theory]
    [InlineData(PowerProfile.Quiet)]
    [InlineData(PowerProfile.Balanced)]
    [InlineData(PowerProfile.Performance)]
    [InlineData(PowerProfile.Turbo)]
    public void GetDefaultGpuCurve_ReturnsValidMonotonicCurve(PowerProfile profile)
    {
        var curve = FanCurveHelper.GetDefaultGpuCurve(profile);

        Assert.NotNull(curve);
        Assert.True(curve.Count >= 4, "Curve must have at least 4 waypoints");

        for (int i = 0; i < curve.Count - 1; i++)
        {
            Assert.True(curve[i].X < curve[i + 1].X, $"Temperatures must strictly increase: {curve[i].X} vs {curve[i + 1].X}");
            Assert.True(curve[i].Y <= curve[i + 1].Y, $"Speeds must monotonically increase: {curve[i].Y} vs {curve[i + 1].Y}");
            Assert.True(curve[i].Y >= 0 && curve[i].Y <= 100, $"Speed must be in [0, 100]: {curve[i].Y}");
        }
    }

    [Theory]
    [InlineData(0, 0)]          // Idle/stopped
    [InlineData(-500, 0)]       // Negative/invalid RPM
    [InlineData(1800, 30)]      // Minimum active threshold (30%)
    [InlineData(5850, 100)]     // Maximum rated tachometer (100%)
    [InlineData(3825, 65)]      // Midpoint: (3825-1800)/4050 * 70 + 30 = 65%
    [InlineData(7000, 100)]     // Above max clamped to 100%
    public void RpmToPercentage_ConvertsTachometerAccurately(int rpm, int expectedPercentage)
    {
        int percentage = FanCurveHelper.RpmToPercentage(rpm);
        Assert.Equal(expectedPercentage, percentage);
    }

    [Fact]
    public void GetClampedSpeed_NullCurve_ReturnsDefault50()
    {
        Assert.Equal(50, FanCurveHelper.GetClampedSpeed(null, 50));
    }

    [Fact]
    public void GetClampedSpeed_EmptyCurve_ReturnsDefault50()
    {
        Assert.Equal(50, FanCurveHelper.GetClampedSpeed(new List<Point>(), 50));
    }

    [Theory]
    [InlineData(30, 20)]
    [InlineData(40, 30)]
    [InlineData(110, 80)]
    public void GetClampedSpeed_ValidCurve_MatchesClampedInterpolation(int temp, int expected)
    {
        var curve = new List<Point>
        {
            new(30, 20),
            new(50, 40),
            new(70, 60),
            new(90, 80),
        };

        int speed = FanCurveHelper.GetClampedSpeed(curve, temp);
        Assert.Equal(expected, speed);
        Assert.Equal(Math.Clamp(FanCurveHelper.InterpolateSpeed(curve, temp), 0, 100), speed);
    }

    [Fact]
    public void GetClampedSpeed_OutOfRangeCurve_ClampsToUnitRange()
    {
        var curve = new List<Point>
        {
            new(30, -20),
            new(90, 150),
        };

        Assert.Equal(0, FanCurveHelper.GetClampedSpeed(curve, 10));
        Assert.Equal(100, FanCurveHelper.GetClampedSpeed(curve, 110));
    }
}
