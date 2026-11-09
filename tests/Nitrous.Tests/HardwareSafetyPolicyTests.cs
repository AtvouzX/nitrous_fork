using Nitrous.Enums;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class HardwareSafetyPolicyTests
{
    [Theory]
    [InlineData(PowerProfile.Eco, false, false)]
    [InlineData(PowerProfile.Eco, true, true)]
    [InlineData(PowerProfile.Quiet, false, false)]
    [InlineData(PowerProfile.Quiet, true, true)]
    [InlineData(PowerProfile.Turbo, false, false)]
    [InlineData(PowerProfile.Turbo, true, true)]
    [InlineData(PowerProfile.Balanced, false, true)]
    [InlineData(PowerProfile.Performance, false, true)]
    public void AllowCustomFanWrite_OnlyLocksFirmwareOwnedModes(
        PowerProfile profile, bool expertMode, bool expected)
    {
        Assert.Equal(expected, HardwareSafetyPolicy.AllowCustomFanWrite(profile, expertMode));
    }

    [Theory]
    // Eco (battery-only: keeps on DC, downgrades to Balanced on AC unconditionally)
    [InlineData(PowerProfile.Eco, false, 100, false, PowerProfile.Eco)]
    [InlineData(PowerProfile.Eco, false, 30, false, PowerProfile.Eco)]
    [InlineData(PowerProfile.Eco, true, 100, false, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Eco, true, 100, true, PowerProfile.Balanced)]
    // Quiet
    [InlineData(PowerProfile.Quiet, false, 100, false, PowerProfile.Quiet)]
    [InlineData(PowerProfile.Quiet, true, 30, false, PowerProfile.Quiet)]
    [InlineData(PowerProfile.Quiet, true, 100, false, PowerProfile.Quiet)]
    // Balanced
    [InlineData(PowerProfile.Balanced, false, 100, false, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Balanced, true, 30, false, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Balanced, true, 100, false, PowerProfile.Balanced)]
    // Performance
    [InlineData(PowerProfile.Performance, false, 100, false, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Performance, true, 30, false, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Performance, true, 100, false, PowerProfile.Performance)]
    // Turbo
    [InlineData(PowerProfile.Turbo, false, 100, false, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Turbo, true, 30, false, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Turbo, true, 100, false, PowerProfile.Turbo)]
    public void ResolveProfileWrite_FullTruthTable_AllTwelveCells(
        PowerProfile requested, bool onAc, int percent, bool expert, PowerProfile expected)
    {
        Assert.Equal(expected, HardwareSafetyPolicy.ResolveProfileWrite(requested, onAc, percent, expert));
    }

    [Theory]
    [InlineData(PowerProfile.Quiet, true, 100, false)]
    [InlineData(PowerProfile.Balanced, true, 100, false)]
    [InlineData(PowerProfile.Performance, true, 100, false)]
    [InlineData(PowerProfile.Turbo, true, 100, false)]
    public void ResolveProfileWrite_AllowsEverythingOnAcWithCharge(
        PowerProfile requested, bool onAc, int percent, bool expert)
    {
        Assert.Equal(requested, HardwareSafetyPolicy.ResolveProfileWrite(requested, onAc, percent, expert));
    }

    [Theory]
    [InlineData(PowerProfile.Performance)]
    [InlineData(PowerProfile.Turbo)]
    public void ResolveProfileWrite_OnBattery_DowngradesOverclockModes(PowerProfile requested)
    {
        Assert.Equal(
            PowerProfile.Balanced,
            HardwareSafetyPolicy.ResolveProfileWrite(requested, onAc: false, batteryPercent: 100, expertMode: false));
    }

    [Theory]
    [InlineData(PowerProfile.Performance)]
    [InlineData(PowerProfile.Turbo)]
    public void ResolveProfileWrite_OnBattery_IgnoresExpertMode(PowerProfile requested)
    {
        Assert.Equal(
            PowerProfile.Balanced,
            HardwareSafetyPolicy.ResolveProfileWrite(requested, onAc: false, batteryPercent: 100, expertMode: true));
    }

    [Fact]
    public void ResolveProfileWrite_BelowThresholdOnAc_Downgrades()
    {
        Assert.Equal(
            PowerProfile.Balanced,
            HardwareSafetyPolicy.ResolveProfileWrite(
                PowerProfile.Performance, onAc: true, batteryPercent: 39, expertMode: false));
    }

    [Fact]
    public void ResolveProfileWrite_AtThresholdOnAc_KeepsMode()
    {
        Assert.Equal(
            PowerProfile.Performance,
            HardwareSafetyPolicy.ResolveProfileWrite(
                PowerProfile.Performance, onAc: true, batteryPercent: 40, expertMode: false));
    }

    [Fact]
    public void ResolveProfileWrite_BelowThresholdWithExpertMode_KeepsMode()
    {
        Assert.Equal(
            PowerProfile.Turbo,
            HardwareSafetyPolicy.ResolveProfileWrite(
                PowerProfile.Turbo, onAc: true, batteryPercent: 10, expertMode: true));
    }

    [Fact]
    public void ResolveProfileWrite_QuietAndBalanced_IgnoreBatteryState()
    {
        Assert.Equal(
            PowerProfile.Quiet,
            HardwareSafetyPolicy.ResolveProfileWrite(
                PowerProfile.Quiet, onAc: false, batteryPercent: 5, expertMode: false));
        Assert.Equal(
            PowerProfile.Balanced,
            HardwareSafetyPolicy.ResolveProfileWrite(
                PowerProfile.Balanced, onAc: false, batteryPercent: 5, expertMode: false));
    }

    [Theory]
    [InlineData(true, 100, false, true)]
    [InlineData(true, 40, false, true)]
    [InlineData(true, 39, false, false)]
    [InlineData(true, 39, true, true)]
    [InlineData(false, 100, false, false)]
    [InlineData(false, 100, true, false)]
    public void AllowsOverclockMode_AlwaysRequiresAc(
        bool onAc, int percent, bool expertMode, bool expected)
    {
        Assert.Equal(expected, HardwareSafetyPolicy.AllowsOverclockMode(onAc, percent, expertMode));
    }

    [Fact]
    public void BatteryPercent_ComputesAndClamps()
    {
        Assert.Equal(50, HardwareSafetyPolicy.BatteryPercent(10000, 5000));
        Assert.Equal(34, HardwareSafetyPolicy.BatteryPercent(10000, 3400));
        Assert.Equal(100, HardwareSafetyPolicy.BatteryPercent(0, 0));
        Assert.Equal(100, HardwareSafetyPolicy.BatteryPercent(5000, 9000));
        Assert.Equal(0, HardwareSafetyPolicy.BatteryPercent(10000, 0));
    }

    [Theory]
    [InlineData(PowerProfile.Eco, true)]
    [InlineData(PowerProfile.Quiet, false)]
    [InlineData(PowerProfile.Balanced, false)]
    [InlineData(PowerProfile.Performance, false)]
    [InlineData(PowerProfile.Turbo, false)]
    public void RequiresBatteryGate_ReturnsTrueOnlyForEco(PowerProfile profile, bool expected)
    {
        Assert.Equal(expected, HardwareSafetyPolicy.RequiresBatteryGate(profile));
    }

    [Fact]
    public void GetNextCycleProfile_OnAc_SkipsEcoAndCyclesAcModes()
    {
        PowerProfile[] supported = [
            PowerProfile.Eco,
            PowerProfile.Quiet,
            PowerProfile.Balanced,
            PowerProfile.Performance,
            PowerProfile.Turbo
        ];

        // On AC with overclock available: Eco is skipped
        Assert.Equal(PowerProfile.Balanced, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Quiet, supported, overclockAvailable: true, onAc: true));
        Assert.Equal(PowerProfile.Performance, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Balanced, supported, overclockAvailable: true, onAc: true));
        Assert.Equal(PowerProfile.Turbo, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Performance, supported, overclockAvailable: true, onAc: true));
        Assert.Equal(PowerProfile.Quiet, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Turbo, supported, overclockAvailable: true, onAc: true));
        // If current is Eco but on AC, cycle advances to Quiet
        Assert.Equal(PowerProfile.Quiet, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Eco, supported, overclockAvailable: true, onAc: true));
    }

    [Fact]
    public void GetNextCycleProfile_OnBattery_IncludesEcoAndSkipsOverclockModes()
    {
        PowerProfile[] supported = [
            PowerProfile.Eco,
            PowerProfile.Quiet,
            PowerProfile.Balanced,
            PowerProfile.Performance,
            PowerProfile.Turbo
        ];

        // On battery: overclock modes (Performance, Turbo) are skipped, Eco is included
        Assert.Equal(PowerProfile.Quiet, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Eco, supported, overclockAvailable: false, onAc: false));
        Assert.Equal(PowerProfile.Balanced, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Quiet, supported, overclockAvailable: false, onAc: false));
        Assert.Equal(PowerProfile.Eco, HardwareSafetyPolicy.GetNextCycleProfile(PowerProfile.Balanced, supported, overclockAvailable: false, onAc: false));
    }
}
