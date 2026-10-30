using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class UnitConverterTests
{
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1000, 1.0)]
    [InlineData(1500, 1.5)]
    [InlineData(-500, -0.5)]
    public void MwToW_ConvertsMilliwattsToWattsExactly(double mw, double expectedW)
    {
        Assert.Equal(expectedW, UnitConverter.MwToW(mw));
    }

    [Theory]
    [InlineData(0u, 0.0)]
    [InlineData(1500u, 1.5)]
    [InlineData(1555u, 1.6)]
    [InlineData(1544u, 1.5)]
    public void MwToWRounded_MatchesNvmlDisplayRounding(uint mw, double expectedW)
    {
        Assert.Equal(expectedW, UnitConverter.MwToWRounded(mw));
        Assert.Equal(Math.Round(mw / 1000.0, 1), UnitConverter.MwToWRounded(mw));
    }

    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(50.0, 50.0)]
    [InlineData(100.0, 100.0)]
    [InlineData(150.0, 100.0)]
    public void ClampPercent_ClampsToUnitRange(double input, double expected)
    {
        Assert.Equal(expected, UnitConverter.ClampPercent(input));
    }

    [Theory]
    [InlineData(50u, 100u, 50.0)]
    [InlineData(0u, 100u, 0.0)]
    [InlineData(100u, 100u, 100.0)]
    [InlineData(150u, 100u, 100.0)]
    [InlineData(50u, 0u, 0.0)]
    public void BatteryPercent_CapacityRatioIsClamped(uint remaining, uint max, double expected)
    {
        Assert.Equal(expected, UnitConverter.BatteryPercent(remaining, max));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 50.0)]
    [InlineData(1.0, 100.0)]
    [InlineData(1.5, 100.0)]
    [InlineData(-0.1, 0.0)]
    public void BatteryPercentFromFraction_MatchesLifePercentScaling(double fraction, double expected)
    {
        Assert.Equal(expected, UnitConverter.BatteryPercentFromFraction(fraction));
        Assert.Equal(Math.Clamp(fraction * 100.0, 0.0, 100.0), UnitConverter.BatteryPercentFromFraction(fraction));
    }
}
