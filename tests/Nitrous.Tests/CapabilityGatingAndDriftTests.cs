using System;
using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

public class CapabilityGatingAndDriftTests : IDisposable
{
    private readonly int _origLastPowerMode;
    private readonly int _origAcPowerMode;
    private readonly int _origDcPowerMode;

    public CapabilityGatingAndDriftTests()
    {
        _origLastPowerMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        _origAcPowerMode = SettingsManager.Get("LastAcPowerMode", (int)PowerProfile.Performance);
        _origDcPowerMode = SettingsManager.Get("LastDcPowerMode", (int)PowerProfile.Quiet);
    }

    public void Dispose()
    {
        AcerWmiManager.SetSupportedProfilesForTesting(null);
        AcerWmiManager.SetActivePowerModeReaderForTesting(null);
        AcerWmiManager.SetTurboSupportedForTesting(null);
        AcerWmiManager.SetPowerStateReaderForTesting(null);
        SettingsManager.Save("LastPowerMode", _origLastPowerMode);
        SettingsManager.Save("LastAcPowerMode", _origAcPowerMode);
        SettingsManager.Save("LastDcPowerMode", _origDcPowerMode);
    }

    [Theory]
    [InlineData(PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Quiet, true)]
    [InlineData(PowerProfile.Performance, PowerProfile.Balanced, PowerProfile.Performance, true)]
    [InlineData(PowerProfile.Turbo, PowerProfile.Balanced, PowerProfile.Turbo, true)]
    [InlineData(PowerProfile.Balanced, PowerProfile.Balanced, PowerProfile.Balanced, false)]
    public void ResolveDrift_WhenHardwareReportsValue_HardwareTruthWins(
        PowerProfile detected, PowerProfile stored, PowerProfile expected, bool expectedDrift)
    {
        Assert.Equal(expected, HardwareSafetyPolicy.ResolveDrift(detected, stored));
        Assert.Equal(expectedDrift, HardwareSafetyPolicy.HasDrift(detected, stored));
    }

    [Fact]
    public void ResolveDrift_WhenHardwareCannotBeRead_PreservesStoredState()
    {
        Assert.Equal(PowerProfile.Performance, HardwareSafetyPolicy.ResolveDrift(null, PowerProfile.Performance));
        Assert.Equal(PowerProfile.Quiet, HardwareSafetyPolicy.ResolveDrift(null, PowerProfile.Quiet));
        Assert.False(HardwareSafetyPolicy.HasDrift(null, PowerProfile.Performance));
    }

    [Fact]
    public void HotkeyCycle_WhenTurboNotSupported_SkipsTurboAndWrapsToQuiet()
    {
        var supported = new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance };

        var next1 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Quiet, supported, overclockAvailable: true);
        Assert.Equal(PowerProfile.Balanced, next1);

        var next2 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Balanced, supported, overclockAvailable: true);
        Assert.Equal(PowerProfile.Performance, next2);

        var next3 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Performance, supported, overclockAvailable: true);
        Assert.Equal(PowerProfile.Quiet, next3); // Turbo skipped!
    }

    [Fact]
    public void HotkeyCycle_WhenTurboSupported_CyclesThroughAllFourModes()
    {
        var supported = new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo };

        var next1 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Quiet, supported, overclockAvailable: true);
        Assert.Equal(PowerProfile.Balanced, next1);

        var next2 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Balanced, supported, overclockAvailable: true);
        Assert.Equal(PowerProfile.Performance, next2);

        var next3 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Performance, supported, overclockAvailable: true);
        Assert.Equal(PowerProfile.Turbo, next3);

        var next4 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Turbo, supported, overclockAvailable: true);
        Assert.Equal(PowerProfile.Quiet, next4);
    }

    [Fact]
    public void HotkeyCycle_OnBattery_SkipsPerformanceAndTurbo()
    {
        var supported = new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo };

        var next1 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Quiet, supported, overclockAvailable: false);
        Assert.Equal(PowerProfile.Balanced, next1);

        var next2 = HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Balanced, supported, overclockAvailable: false);
        Assert.Equal(PowerProfile.Quiet, next2); // Skips Performance and Turbo!
    }

    [Fact]
    public void SupportedProfiles_BecomesSingleSourceOfTruth()
    {
        AcerWmiManager.SetSupportedProfilesForTesting(new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance });

        Assert.False(AcerWmiManager.IsTurboModeSupported());
        Assert.DoesNotContain(PowerProfile.Turbo, AcerWmiManager.GetSupportedProfiles());
        Assert.Contains(PowerProfile.Performance, AcerWmiManager.GetSupportedProfiles());

        AcerWmiManager.SetSupportedProfilesForTesting(new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo });

        Assert.True(AcerWmiManager.IsTurboModeSupported());
        Assert.Contains(PowerProfile.Turbo, AcerWmiManager.GetSupportedProfiles());
    }

    [Fact]
    public void Eco_IsHiddenWhenCapabilityBitIsClear()
    {
        AcerWmiManager.SetSupportedProfilesForTesting(
            new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance });

        Assert.False(AcerWmiManager.IsEcoModeSupported());
        Assert.DoesNotContain(PowerProfile.Eco, AcerWmiManager.GetSupportedProfiles());

        AcerWmiManager.SetSupportedProfilesForTesting(
            new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Eco });

        Assert.True(AcerWmiManager.IsEcoModeSupported());
        Assert.Contains(PowerProfile.Eco, AcerWmiManager.GetSupportedProfiles());
    }

    [Fact]
    public void ScenarioApplyPlan_FollowsSupportedProfiles()
    {
        var profile = new ScenarioProfile
        {
            AcMode = PowerProfile.Turbo,
            DcMode = PowerProfile.Quiet
        };

        // When Turbo is unsupported, falls back to Performance
        var noTurboList = new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance };
        var planNoTurbo = TrayApplication.BuildScenarioApplyPlan(profile, isAcOnline: true, noTurboList);
        Assert.Equal(PowerProfile.Performance, planNoTurbo.EffectiveMode);

        // When Turbo is supported, uses Turbo
        var turboList = new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo };
        var planTurbo = TrayApplication.BuildScenarioApplyPlan(profile, isAcOnline: true, turboList);
        Assert.Equal(PowerProfile.Turbo, planTurbo.EffectiveMode);
    }

    [Fact]
    public void AcTransition_WithDetectedMismatch_UpdatesRegistryAndResyncs()
    {
        SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);
        SettingsManager.Save("LastDcPowerMode", (int)PowerProfile.Performance);
        AcerWmiManager.SetActivePowerModeReaderForTesting(() => PowerProfile.Quiet);

        var tray = new TrayApplication();
        try
        {
            // Simulate transition to DC where hardware is already Quiet
            tray.ApplyPowerSettings(isStartup: false, isOnlineOverride: false);

            Assert.Equal((int)PowerProfile.Quiet, SettingsManager.Get("LastPowerMode", -1));
            Assert.Equal((int)PowerProfile.Quiet, SettingsManager.Get("LastDcPowerMode", -1));
        }
        finally
        {
            tray.Dispose();
        }
    }
}

