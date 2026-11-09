using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class ScenarioCombinerTests
{
    private static ScenarioProfile Profile(string name, PowerProfile ac, PowerProfile dc) => new()
    {
        Name = name,
        AcMode = ac,
        DcMode = dc,
        FanMode = FanProfile.Auto,
        LightingEffect = KeyboardEffect.Static,
        BacklightTimeout = true,
    };

    [Fact]
    public void Combine_TakesHighestPowerModePerSource()
    {
        var a = Profile("Work", PowerProfile.Turbo, PowerProfile.Balanced);
        var b = Profile("Game", PowerProfile.Performance, PowerProfile.Turbo);

        var merged = ScenarioCombiner.Combine([a, b]);

        Assert.Equal(PowerProfile.Turbo, merged.AcMode);
        Assert.Equal(PowerProfile.Turbo, merged.DcMode);
        Assert.Contains("Work", merged.Name);
        Assert.Contains("Game", merged.Name);
    }

    [Fact]
    public void Combine_PowerRank_EcoRanksLowest()
    {
        var a = Profile("Saver", PowerProfile.Balanced, PowerProfile.Eco);
        var b = Profile("Quiet", PowerProfile.Balanced, PowerProfile.Quiet);

        var merged = ScenarioCombiner.Combine([a, b]);

        Assert.Equal(PowerProfile.Quiet, merged.DcMode);
    }

    [Fact]
    public void Combine_FanGoesToStrongestFanSetting()
    {
        var a = Profile("A", PowerProfile.Balanced, PowerProfile.Balanced);
        a.FanMode = FanProfile.Medium;
        a.CustomFanSpeedCpu = 40;
        a.CustomFanSpeedGpu = 45;

        var b = Profile("B", PowerProfile.Balanced, PowerProfile.Balanced);
        b.FanMode = FanProfile.Medium;
        b.CustomFanSpeedCpu = 80;
        b.CustomFanSpeedGpu = 70;

        var merged = ScenarioCombiner.Combine([a, b]);

        Assert.Equal(FanProfile.Medium, merged.FanMode);
        Assert.Equal(80, merged.CustomFanSpeedCpu);
        Assert.Equal(70, merged.CustomFanSpeedGpu);
    }

    [Fact]
    public void Combine_TieOnFanStrength_FirstProfileWins()
    {
        var a = Profile("A", PowerProfile.Balanced, PowerProfile.Balanced);
        a.FanMode = FanProfile.Max;

        var b = Profile("B", PowerProfile.Balanced, PowerProfile.Balanced);
        b.FanMode = FanProfile.Medium;
        b.CustomFanSpeedCpu = 100;
        b.CustomFanSpeedGpu = 100;
        b.FanCurvePreset = "Performance";

        var merged = ScenarioCombiner.Combine([a, b]);

        // Max (100) ties custom 100: Max listed first wins on tie.
        Assert.Equal(FanProfile.Max, merged.FanMode);
    }

    [Fact]
    public void Combine_KeyboardComesFromHighestPowerProfile()
    {
        var a = Profile("Calm", PowerProfile.Balanced, PowerProfile.Balanced);
        a.LightingEffect = KeyboardEffect.Breathing;
        a.BacklightTimeout = false;
        a.ZoneColors = ["#111111", "#111111", "#111111", "#111111"];

        var b = Profile("Strong", PowerProfile.Turbo, PowerProfile.Balanced);
        b.LightingEffect = KeyboardEffect.Wave;
        b.BacklightTimeout = true;
        b.ZoneColors = ["#FF0000", "#FF0000", "#FF0000", "#FF0000"];

        var merged = ScenarioCombiner.Combine([a, b]);

        Assert.Equal(KeyboardEffect.Wave, merged.LightingEffect);
        Assert.True(merged.BacklightTimeout);
        Assert.Equal("#FF0000", merged.ZoneColors[0]);
        Assert.Equal(b.Id, merged.Id);
    }

    [Fact]
    public void Combine_SingleProfile_ReturnsEquivalentProfile()
    {
        var a = Profile("Solo", PowerProfile.Performance, PowerProfile.Quiet);
        a.FanMode = FanProfile.Quiet;

        var merged = ScenarioCombiner.Combine([a]);

        Assert.Equal("Solo", merged.Name);
        Assert.Equal(PowerProfile.Performance, merged.AcMode);
        Assert.Equal(FanProfile.Quiet, merged.FanMode);
    }
}
