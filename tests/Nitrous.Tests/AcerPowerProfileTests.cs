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

    public AcerPowerProfileTests()
    {
        _origLastPowerMode = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        _origCpuFan = SettingsManager.Get("CustomFanSpeedCpu", 50);
        _origGpuFan = SettingsManager.Get("CustomFanSpeedGpu", 50);
    }

    public void Dispose()
    {
        AcerWmiManager.SetWmiInvokeForTesting(null);
        AcerWmiManager.SetMiscSettingReaderForTesting(null);
        AcerWmiManager.SetTurboSupportedForTesting(null);
        SettingsManager.Save("LastPowerMode", _origLastPowerMode);
        SettingsManager.Save("CustomFanSpeedCpu", _origCpuFan);
        SettingsManager.Save("CustomFanSpeedGpu", _origGpuFan);
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
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Balanced);

        Assert.NotEmpty(_wmiCalls);
        Assert.All(_wmiCalls, c => Assert.Equal("SetGamingMiscSetting", c.Method));
    }

    [Fact]
    public async Task ApplyPowerProfileAsync_ClampsFanSpeedsToValidRange()
    {
        AcerWmiManager.SetTurboSupportedForTesting(true);
        RecordWmi();

        await AcerWmiManager.ApplyPowerProfileAsync(PowerProfile.Balanced, 999, -5);

        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (1ul | (100ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (2ul | (0ul << 8)));
        Assert.Contains(_wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (4ul | (0ul << 8)));
    }
}
