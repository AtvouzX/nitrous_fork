using System;
using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class ScenarioStoreTests : IDisposable
{
    private static readonly string[] Keys =
    [
        ScenarioStore.ProfilesKey, ScenarioStore.ActiveIdKey, ScenarioStore.SelectedIdKey,
        ScenarioStore.ApplyRequestKey, ScenarioStore.EnabledKey, ScenarioStore.SuspendedKey,
    ];

    private readonly Dictionary<string, object?> _backup = new();

    public ScenarioStoreTests()
    {
        foreach (string key in Keys)
        {
            _backup[key] = SettingsManager.Get(key, (object?)null);
            SettingsManager.Delete(key);
        }
        SettingsManager.InvalidateCache();
    }

    public void Dispose()
    {
        foreach (string key in Keys)
        {
            if (_backup[key] is { } value) SettingsManager.Save(key, value);
            else SettingsManager.Delete(key);
        }
        SettingsManager.InvalidateCache();
    }

    [Fact]
    public void SeedIfAbsent_CreatesThreePresets_DailyActiveAndSelected_MasterOff()
    {
        var profiles = ScenarioStore.SeedIfAbsent();

        Assert.Equal(3, profiles.Count);
        Assert.Equal("Daily Use", profiles[0].Name);
        Assert.Equal("Gaming", profiles[1].Name);
        Assert.Equal("Quiet Occasion", profiles[2].Name);

        Assert.Equal(PowerProfile.Turbo, profiles[1].AcMode);
        Assert.Equal(PowerProfile.Balanced, profiles[1].DcMode);
        Assert.Equal(KeyboardEffect.Wave, profiles[1].LightingEffect);

        Assert.Equal(PowerProfile.Quiet, profiles[2].AcMode);
        Assert.Equal(PowerProfile.Quiet, profiles[2].DcMode);
        Assert.Equal(KeyboardEffect.Static, profiles[2].LightingEffect);

        Assert.Equal(profiles[0].Id, ScenarioStore.GetActiveId());
        Assert.Equal(profiles[0].Id, ScenarioStore.GetSelectedId());
        Assert.False(ScenarioStore.IsEnabled);

        // Second call does not reseed.
        var again = ScenarioStore.SeedIfAbsent();
        Assert.Equal(profiles[0].Id, again[0].Id);
    }

    [Fact]
    public void SaveLoad_RoundTripsThroughRegistry()
    {
        var p = new ScenarioProfile { Name = "Work", AcMode = PowerProfile.Balanced, DcMode = PowerProfile.Quiet };
        ScenarioStore.SaveProfiles([p]);
        ScenarioStore.SetActiveId(p.Id);
        ScenarioStore.SetSelectedId(p.Id);
        ScenarioStore.SetEnabled(true);
        SettingsManager.InvalidateCache();

        var loaded = ScenarioStore.LoadProfiles();
        var single = Assert.Single(loaded);
        Assert.Equal("Work", single.Name);
        Assert.Equal(p.Id, ScenarioStore.GetActiveId());
        Assert.Equal(p.Id, ScenarioStore.GetSelectedId());
        Assert.True(ScenarioStore.IsEnabled);
    }

    [Fact]
    public void LoadProfiles_CorruptJson_ReturnsEmpty()
    {
        SettingsManager.Save(ScenarioStore.ProfilesKey, "{oops");
        SettingsManager.InvalidateCache();
        Assert.Empty(ScenarioStore.LoadProfiles());
    }

    [Fact]
    public void Diagnostics_ReportsScenarioSection_WithoutNamesOrAppPaths()
    {
        var p = new ScenarioProfile
        {
            Name = "DiagUniqueName123",
            LinkedApps = [@"C:\SecretFolder\DiagUniqueGame.exe"],
        };
        ScenarioStore.SaveProfiles([p]);

        string report = LogManager.ExportSystemDiagnostics();

        Assert.Contains("[SCENARIOS]", report);
        Assert.DoesNotContain("DiagUniqueName123", report);
        Assert.DoesNotContain("DiagUniqueGame.exe", report);
        Assert.DoesNotContain("SecretFolder", report);
    }

    [Fact]
    public void Suspended_RoundTrips()
    {
        Assert.False(ScenarioStore.IsSuspended);
        ScenarioStore.SetSuspended(true);
        Assert.True(ScenarioStore.IsSuspended);
        ScenarioStore.SetSuspended(false);
        Assert.False(ScenarioStore.IsSuspended);
    }

    [Fact]
    public void RequestApply_IncrementsNonce()
    {
        int first = ScenarioStore.RequestApply();
        int second = ScenarioStore.RequestApply();
        Assert.Equal(first + 1, second);
        Assert.Equal(second, ScenarioStore.GetApplyRequest());
    }
}
