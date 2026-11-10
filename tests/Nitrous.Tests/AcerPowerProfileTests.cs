using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class AcerPowerProfileTests : IDisposable
{
    private readonly List<(string Class, string Method, ulong Input)> _wmiCalls = new();
    private readonly int _origLastPowerMode;
    private readonly int _origCpuFan;
    private readonly int _origGpuFan;
    private readonly int _origExpertMode;
    private readonly string _origFanMode;
    private readonly string _origAcFanMode;
    private readonly string _origDcFanMode;

    public AcerPowerProfileTests()
    {
        _origLastPowerMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        _origCpuFan = SettingsManager.Get("CustomFanSpeedCpu", 50);
        _origGpuFan = SettingsManager.Get("CustomFanSpeedGpu", 50);
        _origExpertMode = SettingsManager.Get("ExpertMode", 0);
        _origFanMode = SettingsManager.GetString("LastFanMode", "Auto");
        _origAcFanMode = SettingsManager.GetString("LastAcFanMode", "Auto");
        _origDcFanMode = SettingsManager.GetString("LastDcFanMode", "Auto");
    }

    public void Dispose()
    {
        AcerWmiManager.SetWmiInvokeForTesting(null);
        AcerWmiManager.SetMiscSettingReaderForTesting(null);
        AcerWmiManager.SetTurboSupportedForTesting(null);
        AcerWmiManager.SetPowerStateReaderForTesting(null);
        SettingsManager.Save("LastPowerMode", _origLastPowerMode);
        SettingsManager.Save("CustomFanSpeedCpu", _origCpuFan);
        SettingsManager.Save("CustomFanSpeedGpu", _origGpuFan);
        SettingsManager.Save("ExpertMode", _origExpertMode);
        SettingsManager.Save("LastFanMode", _origFanMode);
        SettingsManager.Save("LastAcFanMode", _origAcFanMode);
        SettingsManager.Save("LastDcFanMode", _origDcFanMode);
    }

    private static void SetAcPower(bool onAc = true, int percent = 100)
    {
        AcerWmiManager.SetPowerStateReaderForTesting(() => (onAc, percent));
    }

    private void RecordWmi()
    {
        AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
        {
            lock (_wmiCalls)
            {
                _wmiCalls.Add((cls, method, input));
            }
            return true;
        });
    }

    [Fact]
    public void EncodePowerMode_MatchesLegacyPayloadFormat()
    {
        Assert.Equal(((ulong)PowerProfile.Quiet << 8) | 0x0B, AcerWmiManager.EncodePowerMode(PowerProfile.Quiet));
        Assert.Equal(((ulong)PowerProfile.Balanced << 8) | 0x0B, AcerWmiManager.EncodePowerMode(PowerProfile.Balanced));
        Assert.Equal(((ulong)PowerProfile.Performance << 8) | 0x0B, AcerWmiManager.EncodePowerMode(PowerProfile.Performance));
        Assert.Equal(((ulong)PowerProfile.Turbo << 8) | 0x0B, AcerWmiManager.EncodePowerMode(PowerProfile.Turbo));
    }

    [Fact]
    public void DecodePowerMode_RoundTripsAllProfiles()
    {
        foreach (PowerProfile profile in Enum.GetValues<PowerProfile>())
        {
            Assert.Equal(profile, AcerWmiManager.DecodePowerMode(AcerWmiManager.EncodePowerMode(profile)));
        }
    }

    [Fact]
    public void DecodePowerMode_ExtractsModeFromRawWmiOutput()
    {
        Assert.Equal(PowerProfile.Performance, AcerWmiManager.DecodePowerMode(0x040Bu));
        Assert.Equal(PowerProfile.Turbo, AcerWmiManager.DecodePowerMode(0x050Bu));
    }

    [Fact]
    public void GetCapabilities_WithSyntheticReader_ReturnsSupportedTurboAndActiveMode()
    {
        AcerWmiManager.SetMiscSettingReaderForTesting(register => register switch
        {
            0x0A => (ulong?)((0x20ul << 8) | 0x0A),
            0x0B => (ulong?)(((ulong)PowerProfile.Balanced << 8) | 0x0B),
            _ => null,
        });

        var caps = AcerWmiManager.GetCapabilities();

        Assert.True(caps.Supported);
        Assert.True(caps.TurboSupported);
        Assert.Equal(PowerProfile.Balanced, caps.ActiveMode);
    }

    [Fact]
    public void GetCapabilities_WhenReaderReturnsNull_ReportsUnsupported()
    {
        AcerWmiManager.SetMiscSettingReaderForTesting(_ => null);

        var caps = AcerWmiManager.GetCapabilities();

        Assert.False(caps.Supported);
        Assert.False(caps.TurboSupported);
        Assert.Null(caps.ActiveMode);
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_DelegatesPowerAndCustomFans()
    {
        SetAcPower();
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Performance, 60, 70);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Performance));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanBehavior"
            && c.Input == AcerWmiManager.FanBehaviorCustom);
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (1ul | (60ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (2ul | (70ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (4ul | (70ul << 8)));
        Assert.Equal((int)PowerProfile.Performance, SettingsManager.Get("LastPowerMode", -1));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_WhenTurboUnsupported_FallsBackToPerformance()
    {
        SetAcPower();
        AcerWmiManager.SetTurboSupportedForTesting(false);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Turbo);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Performance));
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Turbo));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_WithZeroFans_DoesNotTouchFanWmi()
    {
        SetAcPower();
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Balanced);

        Assert.NotEmpty(_wmiCalls);
        Assert.All(_wmiCalls, c => Assert.Equal("SetGamingMiscSetting", c.Method));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_ClampsFanSpeedsToValidRange()
    {
        SetAcPower();
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Balanced, 999, -5);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (1ul | (100ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (2ul | (0ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (4ul | (0ul << 8)));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_InQuietMode_IgnoresCustomFanSpeeds()
    {
        SetAcPower();
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Quiet, 60, 70);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Quiet));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanBehavior"
            && c.Input == AcerWmiManager.FanBehaviorAuto);
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingFanSpeed");
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingFanBehavior"
            && c.Input == AcerWmiManager.FanBehaviorCustom);
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_InQuietMode_ResetsTrackedFanModeToAuto()
    {
        SetAcPower();
        SettingsManager.Save("ExpertMode", 0);
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();
        SettingsManager.Save("LastFanMode", FanProfile.Medium.ToString());

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Quiet);

        Assert.Equal(FanProfile.Auto.ToString(), SettingsManager.GetString("LastFanMode", "Auto"));
        Assert.Equal(FanProfile.Auto.ToString(), SettingsManager.GetString("LastAcFanMode", "Auto"));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_InBalancedMode_KeepsTrackedFanMode()
    {
        SetAcPower();
        SettingsManager.Save("ExpertMode", 0);
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();
        SettingsManager.Save("LastFanMode", FanProfile.Medium.ToString());

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Balanced);

        Assert.Equal(FanProfile.Medium.ToString(), SettingsManager.GetString("LastFanMode", "Auto"));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_InQuietMode_WithExpertMode_WritesCustomFanSpeeds()
    {
        SetAcPower();
        SettingsManager.Save("ExpertMode", 1);
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Quiet, 60, 70);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanBehavior"
            && c.Input == AcerWmiManager.FanBehaviorCustom);
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (1ul | (60ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (2ul | (70ul << 8)));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_OnBattery_DowngradesPerformanceToBalanced()
    {
        SetAcPower(onAc: false, percent: 100);
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Performance);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Balanced));
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Performance));
        Assert.Equal((int)PowerProfile.Balanced, SettingsManager.Get("LastPowerMode", -1));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_BelowFortyPercentOnAc_DowngradesTurboToBalanced()
    {
        SetAcPower(onAc: true, percent: 34);
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Turbo);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Balanced));
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Turbo));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_BelowFortyPercentOnAc_WithExpertMode_KeepsPerformance()
    {
        SetAcPower(onAc: true, percent: 34);
        SettingsManager.Save("ExpertMode", 1);
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Performance);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Performance));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_OnBattery_WithExpertMode_StillDowngrades()
    {
        SetAcPower(onAc: false, percent: 100);
        SettingsManager.Save("ExpertMode", 1);
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Turbo);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Balanced));
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingMiscSetting"
            && c.Input == AcerWmiManager.EncodePowerMode(PowerProfile.Turbo));
    }

    [Theory]
    [InlineData(PowerProfile.Quiet, false, 100, PowerProfile.Quiet)]
    [InlineData(PowerProfile.Balanced, false, 100, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Performance, false, 100, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Turbo, false, 100, PowerProfile.Balanced)]
    [InlineData(PowerProfile.Performance, true, 100, PowerProfile.Performance)]
    [InlineData(PowerProfile.Turbo, true, 39, PowerProfile.Balanced)]
    public void ResolvePowerProfileRequest_FollowsAcGate(
        PowerProfile requested, bool onAc, int percent, PowerProfile expected)
    {
        SetAcPower(onAc, percent);

        Assert.Equal(expected, AcerWmiManager.ResolvePowerProfileRequest(requested));
    }

    [Fact]
    public void IsOverclockModeAvailable_TracksAcState()
    {
        SetAcPower(onAc: true, percent: 100);
        Assert.True(AcerWmiManager.IsOverclockModeAvailable());

        SetAcPower(onAc: false, percent: 100);
        Assert.False(AcerWmiManager.IsOverclockModeAvailable());
    }

    [Fact]
    public async Task SetFansAsync_InQuietMode_IsRefusedAndHandsBackToFirmware()
    {
        SettingsManager.Save("LastPowerMode", (int)PowerProfile.Quiet);
        RecordWmi();

        bool applied = await AcerWmiManager.SetFansAsync(FanProfile.Max);

        Assert.False(applied);
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanBehavior"
            && c.Input == AcerWmiManager.FanBehaviorAuto);
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingFanBehavior"
            && c.Input == AcerWmiManager.FanBehaviorMax);
    }

    [Fact]
    public async Task SetFansAsync_WithExplicitZeroSpeeds_WritesZeroNotProfilePercent()
    {
        SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);
        RecordWmi();

        bool applied = await AcerWmiManager.SetFansAsync(FanProfile.Medium, 0, 0);

        Assert.True(applied);
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (1ul | (0ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (2ul | (0ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (4ul | (0ul << 8)));
        Assert.DoesNotContain(_wmiCalls, c => c.Method == "SetGamingFanSpeed"
            && c.Input == (1ul | ((ulong)FanProfile.Medium << 8)));
    }

    [Fact]
    public async Task SetFansAsync_WithoutSpeeds_StillUsesProfilePercent()
    {
        SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);
        RecordWmi();

        await AcerWmiManager.SetFansAsync(FanProfile.Quiet);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed"
            && c.Input == (1ul | ((ulong)FanProfile.Quiet << 8)));
    }

    [Fact]
    public async Task SetFansAsync_InQuietMode_WithExpertMode_Applies()
    {
        SettingsManager.Save("LastPowerMode", (int)PowerProfile.Quiet);
        SettingsManager.Save("ExpertMode", 1);
        RecordWmi();

        bool applied = await AcerWmiManager.SetFansAsync(FanProfile.Max);

        Assert.True(applied);
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanBehavior"
            && c.Input == AcerWmiManager.FanBehaviorMax);
    }
}
