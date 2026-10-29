using System;
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
        NvidiaGpuManager.MigrateLegacyDefaultOffsets();

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

