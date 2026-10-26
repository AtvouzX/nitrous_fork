using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class FanCurveGraphHelperTests
{
    private const double Width = 400.0;
    private const double Height = 200.0;
    private const double MinTemp = 30.0;
    private const double MaxTemp = 100.0;
    private const double MinSpeed = 0.0;
    private const double MaxSpeed = 100.0;

    [Theory]
    [InlineData(30.0, 0.0)]
    [InlineData(65.0, 200.0)]
    [InlineData(100.0, 400.0)]
    public void TempToCanvasX_ConvertsCorrectly(double temp, double expectedX)
    {
        double x = FanCurveGraphHelper.TempToCanvasX(temp, Width, MinTemp, MaxTemp);
        Assert.Equal(expectedX, x, 3);
    }

    [Theory]
    [InlineData(0.0, 30.0)]
    [InlineData(200.0, 65.0)]
    [InlineData(400.0, 100.0)]
    public void CanvasXToTemp_ConvertsCorrectly(double x, double expectedTemp)
    {
        double temp = FanCurveGraphHelper.CanvasXToTemp(x, Width, MinTemp, MaxTemp);
        Assert.Equal(expectedTemp, temp, 3);
    }

    [Fact]
    public void TempAndCanvasX_RoundTripPreservesValue()
    {
        double originalTemp = 58.7;
        double x = FanCurveGraphHelper.TempToCanvasX(originalTemp, Width, MinTemp, MaxTemp);
        double roundTrip = FanCurveGraphHelper.CanvasXToTemp(x, Width, MinTemp, MaxTemp);
        Assert.Equal(originalTemp, roundTrip, 4);
    }

    [Theory]
    [InlineData(0.0, 200.0)]   // 0% speed is at the bottom (y = height)
    [InlineData(50.0, 100.0)]
    [InlineData(100.0, 0.0)]   // 100% speed is at the top (y = 0)
    public void SpeedToCanvasY_InvertsAxisCorrectly(double speed, double expectedY)
    {
        double y = FanCurveGraphHelper.SpeedToCanvasY(speed, Height, MinSpeed, MaxSpeed);
        Assert.Equal(expectedY, y, 3);
    }

    [Theory]
    [InlineData(200.0, 0.0)]
    [InlineData(100.0, 50.0)]
    [InlineData(0.0, 100.0)]
    public void CanvasYToSpeed_ConvertsCorrectly(double y, double expectedSpeed)
    {
        double speed = FanCurveGraphHelper.CanvasYToSpeed(y, Height, MinSpeed, MaxSpeed);
        Assert.Equal(expectedSpeed, speed, 3);
    }

    [Fact]
    public void SpeedAndCanvasY_RoundTripPreservesValue()
    {
        double originalSpeed = 73.4;
        double y = FanCurveGraphHelper.SpeedToCanvasY(originalSpeed, Height, MinSpeed, MaxSpeed);
        double roundTrip = FanCurveGraphHelper.CanvasYToSpeed(y, Height, MinSpeed, MaxSpeed);
        Assert.Equal(originalSpeed, roundTrip, 4);
    }

    [Theory]
    [InlineData(42.3, 5.0, 40.0)]
    [InlineData(42.6, 5.0, 45.0)]
    [InlineData(50.0, 5.0, 50.0)]
    [InlineData(48.2, 10.0, 50.0)]
    public void SnapToGrid_SnapsCorrectly(double val, double step, double expected)
    {
        double result = FanCurveGraphHelper.SnapToGrid(val, step);
        Assert.Equal(expected, result, 3);
    }

    [Fact]
    public void ConstrainTemperature_ClampsWithinNeighbors()
    {
        // Target is 50, previous node is 40 (with minGap 5 -> min 45), next node is 60 (with minGap 5 -> max 55)
        double clamped = FanCurveGraphHelper.ConstrainTemperature(50, 40, 60, MinTemp, MaxTemp, 5.0);
        Assert.Equal(50, clamped);

        // Lower violation: tries to be 42, clamped to 45
        double lowClamped = FanCurveGraphHelper.ConstrainTemperature(42, 40, 60, MinTemp, MaxTemp, 5.0);
        Assert.Equal(45, lowClamped);

        // Upper violation: tries to be 58, clamped to 55
        double highClamped = FanCurveGraphHelper.ConstrainTemperature(58, 40, 60, MinTemp, MaxTemp, 5.0);
        Assert.Equal(55, highClamped);
    }

    [Fact]
    public void ConstrainTemperature_HandlesBoundaryEndpoints()
    {
        // First node (no prevNode)
        double firstClamped = FanCurveGraphHelper.ConstrainTemperature(25, null, 50, MinTemp, MaxTemp, 5.0);
        Assert.Equal(30, firstClamped); // Clamped to MinTemp

        // Last node (no nextNode)
        double lastClamped = FanCurveGraphHelper.ConstrainTemperature(105, 80, null, MinTemp, MaxTemp, 5.0);
        Assert.Equal(100, lastClamped); // Clamped to MaxTemp
    }

    [Theory]
    [InlineData(-10.0, 0.0)]
    [InlineData(45.0, 45.0)]
    [InlineData(120.0, 100.0)]
    public void ConstrainSpeed_ClampsToRange(double speed, double expected)
    {
        double result = FanCurveGraphHelper.ConstrainSpeed(speed, MinSpeed, MaxSpeed);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ZeroCanvasDimensions_ReturnsSafeDefaults()
    {
        Assert.Equal(0.0, FanCurveGraphHelper.TempToCanvasX(50, 0));
        Assert.Equal(MinTemp, FanCurveGraphHelper.CanvasXToTemp(50, 0, MinTemp, MaxTemp));
        Assert.Equal(0.0, FanCurveGraphHelper.SpeedToCanvasY(50, 0));
        Assert.Equal(MinSpeed, FanCurveGraphHelper.CanvasYToSpeed(50, 0, MinSpeed, MaxSpeed));
    }
}
