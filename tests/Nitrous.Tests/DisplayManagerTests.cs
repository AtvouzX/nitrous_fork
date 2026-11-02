using System;
using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class DisplayManagerTests
{
    [Fact]
    public void GetPrimaryMaxRefreshRate_ReturnsAtLeast60Hz()
    {
        int maxHz = DisplayManager.GetPrimaryMaxRefreshRate();
        Assert.True(maxHz >= 60, $"Expected primary max refresh rate to be at least 60Hz, but was {maxHz}");
    }

    [Fact]
    public void IsRefreshSupported_InvalidDimensions_ReturnsFalse()
    {
        bool supported = DisplayManager.IsRefreshSupported(-1, -1, 9999);
        Assert.False(supported);
    }

    [Fact]
    public void TryFindMode_InvalidDimensions_ReturnsFalse()
    {
        bool found = DisplayManager.TryFindMode(-1, -1, 9999, out _);
        Assert.False(found);
    }

    [Theory]
    [InlineData(RefreshProfile.Auto, true, 144, 144)]
    [InlineData(RefreshProfile.Auto, false, 144, 60)]
    [InlineData(RefreshProfile.Hz60, true, 144, 60)]
    [InlineData(RefreshProfile.Hz60, false, 144, 60)]
    [InlineData(RefreshProfile.MaxHz, true, 144, 144)]
    [InlineData(RefreshProfile.MaxHz, false, 144, 144)]
    public void ResolveTargetRefreshRate_ComputesExpectedFrequency(RefreshProfile profile, bool isAcPower, int maxHz, int expectedHz)
    {
        int targetHz = DisplayManager.ResolveTargetRefreshRate(profile, isAcPower, maxHz);
        Assert.Equal(expectedHz, targetHz);
    }

    [Fact]
    public void GetSupportedRefreshRates_ReturnsNonNullList()
    {
        var rates = DisplayManager.GetSupportedRefreshRates();
        Assert.NotNull(rates);
    }

    [Fact]
    public void LegacyDefaultOffsetsMigration_ResetsKnownPresetsToStock()
    {
        // Setup legacy presets
        SettingsManager.Save("GpuCore_Balanced", 50);
        SettingsManager.Save("GpuMemory_Balanced", 100);
        SettingsManager.Save("GpuCore_Quiet", -100);
        SettingsManager.Save("GpuMemory_Quiet", -200);
        SettingsManager.Save("GpuCore_Turbo", 150);
        SettingsManager.Save("GpuMemory_Turbo", 300);
        SettingsManager.Save("GpuOffsetsMigratedToStock_v1", 0);

        // Run migration
        new NvidiaGpuManager(); // ctor runs private legacy migration once

        // Verify stock offsets (0/0)
        Assert.Equal(0, SettingsManager.Get("GpuCore_Balanced", -1));
        Assert.Equal(0, SettingsManager.Get("GpuMemory_Balanced", -1));
        Assert.Equal(0, SettingsManager.Get("GpuCore_Quiet", -1));
        Assert.Equal(0, SettingsManager.Get("GpuMemory_Quiet", -1));
        Assert.Equal(0, SettingsManager.Get("GpuCore_Turbo", -1));
        Assert.Equal(0, SettingsManager.Get("GpuMemory_Turbo", -1));
        Assert.Equal(1, SettingsManager.Get("GpuOffsetsMigratedToStock_v1", 0));
    }
}
