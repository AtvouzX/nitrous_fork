using System;
using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class ScenarioProfileCodecTests
{
    private static ScenarioProfile Profile(string name) => new()
    {
        Name = name,
        AcMode = PowerProfile.Performance,
        DcMode = PowerProfile.Quiet,
        FanMode = FanProfile.Medium,
        CustomFanSpeedCpu = 60,
        CustomFanSpeedGpu = 70,
        FanCurvePreset = "Auto",
        BacklightTimeout = true,
        LightingEffect = KeyboardEffect.Wave,
        ZoneColors = ["#FF0000", "#00FF00", "#0000FF", "#FFFFFF"],
        LinkedApps = [@"C:\Games\Foo\game.exe"],
        AppSyncEnabled = true,
    };

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = Profile("Gaming");
        string json = ScenarioProfileCodec.Serialize([original]);
        var loaded = ScenarioProfileCodec.Deserialize(json);

        var p = Assert.Single(loaded);
        Assert.Equal(original.Id, p.Id);
        Assert.Equal("Gaming", p.Name);
        Assert.Equal(PowerProfile.Performance, p.AcMode);
        Assert.Equal(PowerProfile.Quiet, p.DcMode);
        Assert.Equal(FanProfile.Medium, p.FanMode);
        Assert.Equal(60, p.CustomFanSpeedCpu);
        Assert.Equal(70, p.CustomFanSpeedGpu);
        Assert.Equal("Auto", p.FanCurvePreset);
        Assert.True(p.BacklightTimeout);
        Assert.Equal(KeyboardEffect.Wave, p.LightingEffect);
        Assert.Equal(["#FF0000", "#00FF00", "#0000FF", "#FFFFFF"], p.ZoneColors);
        Assert.Equal([@"C:\Games\Foo\game.exe"], p.LinkedApps);
        Assert.True(p.AppSyncEnabled);
    }

    [Fact]
    public void Deserialize_NullOrCorrupt_ReturnsEmpty()
    {
        Assert.Empty(ScenarioProfileCodec.Deserialize(null));
        Assert.Empty(ScenarioProfileCodec.Deserialize(""));
        Assert.Empty(ScenarioProfileCodec.Deserialize("{not json"));
        Assert.Empty(ScenarioProfileCodec.Deserialize("42"));
    }

    [Fact]
    public void Sanitize_ClampsSpeedsAndCapsCounts()
    {
        var profiles = new List<ScenarioProfile>();
        for (int i = 0; i < 20; i++)
        {
            var p = Profile($"P{i}");
            p.CustomFanSpeedCpu = 250;
            p.CustomFanSpeedGpu = -5;
            for (int a = 0; a < 15; a++) p.LinkedApps.Add($@"C:\A\app{a}.exe");
            profiles.Add(p);
        }

        var clean = ScenarioProfileCodec.Sanitize(profiles);

        Assert.Equal(ScenarioProfileCodec.MaxProfiles, clean.Count);
        Assert.All(clean, p =>
        {
            Assert.Equal(100, p.CustomFanSpeedCpu);
            Assert.Equal(0, p.CustomFanSpeedGpu);
            Assert.True(p.LinkedApps.Count <= ScenarioProfileCodec.MaxLinkedApps);
        });
    }

    [Fact]
    public void Sanitize_DedupesNamesAndClaimsAppsExclusively()
    {
        var a = Profile("Daily");
        var b = Profile("daily");
        b.LinkedApps = [@"D:\Other\GAME.EXE"]; // same file name as a's link, different folder/case

        var clean = ScenarioProfileCodec.Sanitize([a, b]);

        Assert.Equal("Daily", clean[0].Name);
        Assert.False(string.Equals(clean[0].Name, clean[1].Name, StringComparison.OrdinalIgnoreCase));
        Assert.Single(clean[0].LinkedApps);
        Assert.Empty(clean[1].LinkedApps);
    }

    [Fact]
    public void Sanitize_InvalidEnumsAndColors_FallBackToSafeDefaults()
    {
        var p = Profile("X");
        p.AcMode = (PowerProfile)999;
        p.DcMode = (PowerProfile)999;
        p.FanMode = (FanProfile)999;
        p.LightingEffect = (KeyboardEffect)250;
        p.ZoneColors = ["red", "#12345", "#GGGGGG"];
        p.FanCurvePreset = "bad;name,with/seps";

        var clean = ScenarioProfileCodec.Sanitize([p]);
        var c = Assert.Single(clean);

        Assert.Equal(PowerProfile.Balanced, c.AcMode);
        Assert.Equal(PowerProfile.Balanced, c.DcMode);
        Assert.Equal(FanProfile.Auto, c.FanMode);
        Assert.Equal(KeyboardEffect.Static, c.LightingEffect);
        Assert.Equal(4, c.ZoneColors.Count);
        Assert.All(c.ZoneColors, z => Assert.Equal("#FFFFFF", z));
        Assert.DoesNotContain(';', c.FanCurvePreset);
        Assert.DoesNotContain(',', c.FanCurvePreset);
        Assert.False(string.IsNullOrWhiteSpace(c.Id));
    }

    [Fact]
    public void Sanitize_MissingIds_GetUniqueIds()
    {
        var a = Profile("A"); a.Id = "";
        var b = Profile("B"); b.Id = a.Id;

        var clean = ScenarioProfileCodec.Sanitize([a, b]);

        Assert.False(string.IsNullOrWhiteSpace(clean[0].Id));
        Assert.False(string.IsNullOrWhiteSpace(clean[1].Id));
        Assert.NotEqual(clean[0].Id, clean[1].Id);
    }

    [Fact]
    public void Sanitize_EcoMode_DowngradesAcModeToBalanced_AndPreservesDcMode()
    {
        var p = Profile("EcoTest");
        p.AcMode = PowerProfile.Eco;
        p.DcMode = PowerProfile.Eco;

        var clean = ScenarioProfileCodec.Sanitize([p]);
        var c = Assert.Single(clean);

        Assert.Equal(PowerProfile.Balanced, c.AcMode);
        Assert.Equal(PowerProfile.Eco, c.DcMode);
    }
}
