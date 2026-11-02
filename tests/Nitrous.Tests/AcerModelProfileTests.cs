using System;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class AcerModelProfileTests
{
    [Fact]
    public void For_MatchesExactModelCodeBoundary()
    {
        var profile = AcerModelProfile.For("Nitro AN16S-61");
        Assert.Equal("AN16S-61", profile.ModelCode);
        Assert.False(profile.RequiresBalancedModeReset);

        // Does not match prefix/suffix with alphanumeric attached
        var mismatch = AcerModelProfile.For("AN16S-611");
        Assert.Equal(AcerModelProfile.Unknown, mismatch);
    }

    [Fact]
    public void For_NullOrEmpty_ReturnsUnknown()
    {
        Assert.Equal(AcerModelProfile.Unknown, AcerModelProfile.For(null));
        Assert.Equal(AcerModelProfile.Unknown, AcerModelProfile.For(""));
        Assert.Equal(AcerModelProfile.Unknown, AcerModelProfile.For("   "));
    }

    [Fact]
    public void Capabilities_PowerModeChannelAndTurboEvaluation()
    {
        // 1. EC HID available with Turbo
        var ecHidWithTurbo = new EcHidInfo(
            "path", 0x1025, 0x174B, 0xFF05, 1, 65, "0.6", 4,
            new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo });

        var caps = new AcerCapabilities(
            "Acer", "TestModel", "1.00", AcerSmbios.Empty, AcerWmiSurface.Empty, ecHidWithTurbo, AcerModelProfile.Unknown);

        Assert.Equal(PowerModeChannel.EcHid, caps.PowerModeChannel);
        Assert.True(caps.CanSetPowerMode);
        Assert.True(caps.SupportsTurbo);

        // 2. EC HID available without Turbo
        var ecHidNoTurbo = new EcHidInfo(
            "path", 0x1025, 0x174B, 0xFF05, 1, 65, "0.6", 3,
            new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance });

        var capsNoTurbo = new AcerCapabilities(
            "Acer", "TestModel", "1.00", AcerSmbios.Empty, AcerWmiSurface.Empty, ecHidNoTurbo, AcerModelProfile.Unknown);

        Assert.Equal(PowerModeChannel.EcHid, capsNoTurbo.PowerModeChannel);
        Assert.True(capsNoTurbo.CanSetPowerMode);
        Assert.False(capsNoTurbo.SupportsTurbo);

        // 3. WMI fallback
        var wmiSurface = new AcerWmiSurface(
            true, false, new[] { "SetGamingMiscSetting" },
            new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo },
            AcerActionFunction.Unknown);

        var capsWmi = new AcerCapabilities(
            "Acer", "TestModel", "1.00", AcerSmbios.Empty, wmiSurface, null, AcerModelProfile.Unknown);

        Assert.Equal(PowerModeChannel.Wmi, capsWmi.PowerModeChannel);
        Assert.True(capsWmi.CanSetPowerMode);
        Assert.True(capsWmi.SupportsTurbo);
    }
}
