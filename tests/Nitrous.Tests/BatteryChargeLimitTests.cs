using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class BatteryChargeLimitTests : IDisposable
{
    private readonly List<(byte No, byte Mask, byte Status)> _healthCalls = new();
    private readonly int _origChargeLimit;

    public BatteryChargeLimitTests()
    {
        _origChargeLimit = SettingsManager.Get("ChargeLimit", 0);
    }

    public void Dispose()
    {
        BatteryManager.SetHealthControlForTesting(null);
        BatteryManager.SetHealthReaderForTesting(null);
        SettingsManager.Save("ChargeLimit", _origChargeLimit);
    }

    [Fact]
    public void ComputeBatteryHealth_WithFullOverDesigned_ClampsToHundred()
    {
        var info = BatteryManager.ComputeBatteryHealth(50000, 60000, 100);

        Assert.NotNull(info);
        Assert.Equal(100.0, info.Value.HealthPercent);
        Assert.Equal(0.0, info.Value.WearPercent);
        Assert.Equal(50000u, info.Value.DesignedCapacity);
        Assert.Equal(60000u, info.Value.FullChargedCapacity);
        Assert.Equal(100u, info.Value.CycleCount);
    }

    [Fact]
    public void ComputeBatteryHealth_WithNormalValues_ComputesWear()
    {
        var info = BatteryManager.ComputeBatteryHealth(50000, 45000, 200);

        Assert.NotNull(info);
        Assert.Equal(90.0, info.Value.HealthPercent, 3);
        Assert.Equal(10.0, info.Value.WearPercent, 3);
    }

    [Fact]
    public void ComputeBatteryHealth_WithZeroDesigned_ReturnsNull()
    {
        Assert.Null(BatteryManager.ComputeBatteryHealth(0, 45000, 0));
    }

    [Fact]
    public async Task SetChargeLimitAsync_DelegatesToHealthControlAndPersistsSetting()
    {
        BatteryManager.SetHealthControlForTesting((no, mask, status) =>
        {
            lock (_healthCalls)
            {
                _healthCalls.Add((no, mask, status));
            }
            return true;
        });

        await BatteryManager.SetChargeLimitAsync(true);

        Assert.Single(_healthCalls);
        Assert.Equal((1, 1, 1), (_healthCalls[0].No, _healthCalls[0].Mask, _healthCalls[0].Status));
        Assert.Equal(1, SettingsManager.Get("ChargeLimit", -1));
    }

    [Fact]
    public async Task SetChargeLimitAsync_WhenDisabled_SendsZeroStatus()
    {
        BatteryManager.SetHealthControlForTesting((no, mask, status) =>
        {
            lock (_healthCalls)
            {
                _healthCalls.Add((no, mask, status));
            }
            return true;
        });

        await BatteryManager.SetChargeLimitAsync(false);

        Assert.Single(_healthCalls);
        Assert.Equal(0, _healthCalls[0].Status);
        Assert.Equal(0, SettingsManager.Get("ChargeLimit", -1));
    }

    [Fact]
    public void GetBatteryHealth_WithSyntheticReader_ReturnsComputedValues()
    {
        BatteryManager.SetHealthReaderForTesting((cls, prop) => (cls, prop) switch
        {
            ("BatteryStaticData", "DesignedCapacity") => (uint?)50000,
            ("BatteryFullChargedCapacity", "FullChargedCapacity") => (uint?)45000,
            ("BatteryCycleCount", "CycleCount") => (uint?)200,
            _ => null,
        });

        var info = BatteryManager.GetBatteryHealth();

        Assert.NotNull(info);
        Assert.Equal(50000u, info.Value.DesignedCapacity);
        Assert.Equal(45000u, info.Value.FullChargedCapacity);
        Assert.Equal(200u, info.Value.CycleCount);
        Assert.Equal(90.0, info.Value.HealthPercent, 3);
    }

    [Fact]
    public void GetBatteryHealth_WithZeroDesigned_ReturnsNull()
    {
        BatteryManager.SetHealthReaderForTesting((cls, prop) => (cls, prop) switch
        {
            ("BatteryStaticData", "DesignedCapacity") => (uint?)0,
            ("BatteryFullChargedCapacity", "FullChargedCapacity") => (uint?)0,
            ("BatteryCycleCount", "CycleCount") => (uint?)0,
            _ => null,
        });

        Assert.Null(BatteryManager.GetBatteryHealth());
    }
}
