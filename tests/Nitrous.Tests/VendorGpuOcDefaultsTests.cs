using System;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class VendorGpuOcDefaultsTests : IDisposable
{
    private readonly int _origToggle;
    private readonly string _origMigrated;
    private readonly (int Core, int Memory, string Source)[] _origProfiles;

    private static readonly PowerProfile[] Profiles =
        [PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo];

    public VendorGpuOcDefaultsTests()
    {
        _origToggle = SettingsManager.GetInt(VendorGpuOcDefaults.ToggleSettingKey, 0);
        _origMigrated = SettingsManager.GetString("GpuSourceMigrated_v1", string.Empty);
        _origProfiles = new (int, int, string)[Profiles.Length];
        for (int i = 0; i < Profiles.Length; i++)
        {
            var profile = Profiles[i];
            _origProfiles[i] = (
                SettingsManager.GetInt($"GpuCore_{profile}", 0),
                SettingsManager.GetInt($"GpuMemory_{profile}", 0),
                SettingsManager.GetString(VendorGpuOcDefaults.SourceKey(profile), string.Empty));
        }
    }

    public void Dispose()
    {
        SettingsManager.Save(VendorGpuOcDefaults.ToggleSettingKey, _origToggle);
        SettingsManager.Save("GpuSourceMigrated_v1", _origMigrated);
        for (int i = 0; i < Profiles.Length; i++)
        {
            var profile = Profiles[i];
            SettingsManager.Save($"GpuCore_{profile}", _origProfiles[i].Core);
            SettingsManager.Save($"GpuMemory_{profile}", _origProfiles[i].Memory);
            SettingsManager.Save(VendorGpuOcDefaults.SourceKey(profile), _origProfiles[i].Source);
        }
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_17581025&REV_A1", "DEV_28A0&SUBSYS_17581025")]
    [InlineData(@"pci\ven_10de&dev_28a0&subsys_17581025&rev_a1", "DEV_28A0&SUBSYS_17581025")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_17581025&REV_A1\4&2B3F0E5A&0&0008", "DEV_28A0&SUBSYS_17581025")]
    [InlineData("DEV_28A0&SUBSYS_17581025", "DEV_28A0&SUBSYS_17581025")]
    [InlineData("dev_28a0&subsys_17581025", "DEV_28A0&SUBSYS_17581025")]
    public void NormalizePciId_ExtractsDeviceAndSubsystem(string raw, string expected)
    {
        Assert.Equal(expected, VendorGpuOcDefaults.NormalizePciId(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0")]
    [InlineData("DEV_ZZZZ&SUBSYS_17581025")]
    [InlineData("DEV_28A0&SUBSYS_1758102")]
    [InlineData("DEV_28A0&SUBSYS_17581025X")]
    [InlineData("garbage")]
    public void NormalizePciId_RejectsMalformedInput(string? raw)
    {
        Assert.Null(VendorGpuOcDefaults.NormalizePciId(raw));
    }

    [Fact]
    public void VendorTable_CoversEveryIdFromTheVendorPackage()
    {
        // 41 SKU .ini files collapse to 140 distinct dev+subsystem pairs and 5 tables.
        Assert.Equal(5, VendorGpuOcDefaults.TableCount);
        Assert.Equal(140, VendorGpuOcDefaults.KnownPciIdCount);
    }

    [Theory]
    // Table 0: Turbo only
    [InlineData("DEV_25AC&SUBSYS_16861025", 0, 0, 0, 0, 100, 200)]
    // Table 1: Performance and Turbo +100/+200
    [InlineData("DEV_28A0&SUBSYS_17581025", 0, 0, 100, 200, 100, 200)]
    // Table 2: Turbo +100/0 only
    [InlineData("DEV_25AC&SUBSYS_172C1025", 0, 0, 0, 0, 100, 0)]
    // Table 3: Performance +50/0, Turbo +100/0
    [InlineData("DEV_25AC&SUBSYS_17471025", 0, 0, 50, 0, 100, 0)]
    // Table 4: Performance and Turbo +100/0
    [InlineData("DEV_25AC&SUBSYS_17581025", 0, 0, 100, 0, 100, 0)]
    public void GetOffsets_ResolvesKnownSku(
        string pciId,
        int quietCore, int quietMemory,
        int performanceCore, int performanceMemory,
        int turboCore, int turboMemory)
    {
        Assert.Equal((quietCore, quietMemory), VendorGpuOcDefaults.GetOffsets(pciId, PowerProfile.Quiet));
        Assert.Equal((performanceCore, performanceMemory), VendorGpuOcDefaults.GetOffsets(pciId, PowerProfile.Performance));
        Assert.Equal((turboCore, turboMemory), VendorGpuOcDefaults.GetOffsets(pciId, PowerProfile.Turbo));
    }

    [Fact]
    public void GetOffsets_AcceptsFullPnpDeviceId()
    {
        var offsets = VendorGpuOcDefaults.GetOffsets(
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_17581025&REV_A1", PowerProfile.Turbo);

        Assert.Equal((100, 200), offsets);
    }

    [Theory]
    [InlineData("DEV_FFFF&SUBSYS_00000000")]
    [InlineData(null)]
    [InlineData("not a pci id")]
    public void GetOffsets_UnlistedPciFallsBackToStock(string? pciId)
    {
        foreach (var profile in Profiles)
        {
            Assert.Equal((0, 0), VendorGpuOcDefaults.GetOffsets(pciId, profile));
        }
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 100, 200)]
    [InlineData(1, 0, 0, 100, 200, 100, 200)]
    [InlineData(2, 0, 0, 0, 0, 100, 0)]
    [InlineData(3, 0, 0, 50, 0, 100, 0)]
    [InlineData(4, 0, 0, 100, 0, 100, 0)]
    public void GetOffsetsByTableIndex_ResolvesEveryTable(
        int tableIndex,
        int quietCore, int quietMemory,
        int performanceCore, int performanceMemory,
        int turboCore, int turboMemory)
    {
        Assert.Equal((quietCore, quietMemory), VendorGpuOcDefaults.GetOffsetsByTableIndex(tableIndex, PowerProfile.Quiet));
        Assert.Equal((performanceCore, performanceMemory), VendorGpuOcDefaults.GetOffsetsByTableIndex(tableIndex, PowerProfile.Performance));
        Assert.Equal((turboCore, turboMemory), VendorGpuOcDefaults.GetOffsetsByTableIndex(tableIndex, PowerProfile.Turbo));
    }

    [Theory]
    [InlineData(100, 200, VendorGpuOcSource.Custom)]
    [InlineData(0, 200, VendorGpuOcSource.Custom)]
    [InlineData(-50, 0, VendorGpuOcSource.Custom)]
    [InlineData(0, 0, VendorGpuOcSource.Default)]
    public void InferLegacySource_TreatsNonZeroAsCustom(int core, int memory, VendorGpuOcSource expected)
    {
        Assert.Equal(expected, VendorGpuOcDefaults.InferLegacySource(core, memory));
    }

    [Theory]
    // Enable seeds non-custom profiles with the vendor values
    [InlineData(VendorGpuOcSource.Default, true, VendorGpuOcToggleAction.SeedVendor, VendorGpuOcSource.Vendor)]
    [InlineData(VendorGpuOcSource.Vendor, true, VendorGpuOcToggleAction.SeedVendor, VendorGpuOcSource.Vendor)]
    // Custom is never touched in either direction
    [InlineData(VendorGpuOcSource.Custom, true, VendorGpuOcToggleAction.None, VendorGpuOcSource.Custom)]
    [InlineData(VendorGpuOcSource.Custom, false, VendorGpuOcToggleAction.None, VendorGpuOcSource.Custom)]
    // Disable resets only profiles the toggle seeded
    [InlineData(VendorGpuOcSource.Vendor, false, VendorGpuOcToggleAction.ResetStock, VendorGpuOcSource.Default)]
    [InlineData(VendorGpuOcSource.Default, false, VendorGpuOcToggleAction.None, VendorGpuOcSource.Default)]
    public void ResolveTogglePlan_TruthTable(
        VendorGpuOcSource current,
        bool enable,
        VendorGpuOcToggleAction expectedAction,
        VendorGpuOcSource expectedSource)
    {
        var plan = VendorGpuOcDefaults.ResolveTogglePlan(current, enable, 100, 200);

        Assert.Equal(expectedAction, plan.Action);
        Assert.Equal(expectedSource, plan.ResultSource);
    }

    [Fact]
    public void ResolveTogglePlan_ExplicitZeroCustomSurvivesEnable()
    {
        // The regression this model exists for: an explicit user 0/0 must not be
        // overwritten by the vendor table just because it equals stock.
        var plan = VendorGpuOcDefaults.ResolveTogglePlan(VendorGpuOcSource.Custom, true, 100, 200);

        Assert.Equal(VendorGpuOcToggleAction.None, plan.Action);
        Assert.Equal(VendorGpuOcSource.Custom, plan.ResultSource);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void AllowsImmediateApply_RequiresAwakeGpuAndAc(bool awake, bool ac, bool expected)
    {
        Assert.Equal(expected, VendorGpuOcDefaults.AllowsImmediateApply(awake, ac));
    }

    [Fact]
    public void DescribeOffsets_NamesExactOffsetsForKnownSku()
    {
        string text = VendorGpuOcDefaults.DescribeOffsets("DEV_28A0&SUBSYS_17581025");

        Assert.Contains("Performance +100/+200", text);
        Assert.Contains("Turbo +100/+200", text);
    }

    [Fact]
    public void DescribeOffsets_UnlistedSkuStaysStock()
    {
        Assert.Contains("not listed", VendorGpuOcDefaults.DescribeOffsets("DEV_FFFF&SUBSYS_00000000"));
    }

    [Fact]
    public void ApplyVendorGpuDefaultsToSettings_EnableSeedsNonCustomProfiles()
    {
        ResetAllProfiles(VendorGpuOcSource.Default, core: 0, memory: 0);
        using var manager = new NvidiaGpuManager();

        bool changed = manager.ApplyVendorGpuDefaultsToSettings(
            enable: true, pciId: @"PCI\VEN_10DE&DEV_28A0&SUBSYS_17581025&REV_A1");

        Assert.True(changed);
        Assert.Equal(1, SettingsManager.GetInt(VendorGpuOcDefaults.ToggleSettingKey, 0));
        AssertProfile(PowerProfile.Quiet, 0, 0, VendorGpuOcSource.Vendor);
        AssertProfile(PowerProfile.Balanced, 0, 0, VendorGpuOcSource.Vendor);
        AssertProfile(PowerProfile.Performance, 100, 200, VendorGpuOcSource.Vendor);
        AssertProfile(PowerProfile.Turbo, 100, 200, VendorGpuOcSource.Vendor);
    }

    [Fact]
    public void ApplyVendorGpuDefaultsToSettings_CustomProfileSurvivesEnable()
    {
        ResetAllProfiles(VendorGpuOcSource.Default, core: 0, memory: 0);
        SettingsManager.Save(VendorGpuOcDefaults.SourceKey(PowerProfile.Performance), "Custom");
        SettingsManager.Save("GpuCore_Performance", 0);
        SettingsManager.Save("GpuMemory_Performance", 0);
        using var manager = new NvidiaGpuManager();

        manager.ApplyVendorGpuDefaultsToSettings(enable: true, pciId: "DEV_28A0&SUBSYS_17581025");

        AssertProfile(PowerProfile.Performance, 0, 0, VendorGpuOcSource.Custom);
        AssertProfile(PowerProfile.Turbo, 100, 200, VendorGpuOcSource.Vendor);
    }

    [Fact]
    public void ApplyVendorGpuDefaultsToSettings_DisableResetsOnlyVendorProfiles()
    {
        ResetAllProfiles(VendorGpuOcSource.Vendor, core: 100, memory: 200);
        SettingsManager.Save(VendorGpuOcDefaults.SourceKey(PowerProfile.Performance), "Custom");
        SettingsManager.Save("GpuCore_Performance", 42);
        SettingsManager.Save("GpuMemory_Performance", 0);
        using var manager = new NvidiaGpuManager();

        manager.ApplyVendorGpuDefaultsToSettings(enable: false, pciId: "DEV_28A0&SUBSYS_17581025");

        AssertProfile(PowerProfile.Performance, 42, 0, VendorGpuOcSource.Custom);
        AssertProfile(PowerProfile.Quiet, 0, 0, VendorGpuOcSource.Default);
        AssertProfile(PowerProfile.Balanced, 0, 0, VendorGpuOcSource.Default);
        AssertProfile(PowerProfile.Turbo, 0, 0, VendorGpuOcSource.Default);
    }

    [Fact]
    public void ApplyVendorGpuDefaultsToSettings_RoundTripReturnsNonCustomProfilesToStock()
    {
        ResetAllProfiles(VendorGpuOcSource.Default, core: 0, memory: 0);
        using var manager = new NvidiaGpuManager();

        manager.ApplyVendorGpuDefaultsToSettings(enable: true, pciId: "DEV_28A0&SUBSYS_17581025");
        manager.ApplyVendorGpuDefaultsToSettings(enable: false, pciId: "DEV_28A0&SUBSYS_17581025");

        foreach (var profile in Profiles)
        {
            AssertProfile(profile, 0, 0, VendorGpuOcSource.Default);
        }
    }

    [Fact]
    public void ApplyVendorGpuDefaultsToSettings_UnlistedSkuSeedsStock()
    {
        ResetAllProfiles(VendorGpuOcSource.Default, core: 0, memory: 0);
        using var manager = new NvidiaGpuManager();

        manager.ApplyVendorGpuDefaultsToSettings(enable: true, pciId: "DEV_FFFF&SUBSYS_00000000");

        foreach (var profile in Profiles)
        {
            AssertProfile(profile, 0, 0, VendorGpuOcSource.Vendor);
        }
    }

    private static void ResetAllProfiles(VendorGpuOcSource source, int core, int memory)
    {
        foreach (var profile in Profiles)
        {
            SettingsManager.Save($"GpuCore_{profile}", core);
            SettingsManager.Save($"GpuMemory_{profile}", memory);
            SettingsManager.Save(VendorGpuOcDefaults.SourceKey(profile), VendorGpuOcDefaults.FormatSource(source));
        }
    }

    private static void AssertProfile(PowerProfile profile, int core, int memory, VendorGpuOcSource source)
    {
        Assert.Equal(core, SettingsManager.GetInt($"GpuCore_{profile}", int.MinValue));
        Assert.Equal(memory, SettingsManager.GetInt($"GpuMemory_{profile}", int.MinValue));
        Assert.Equal(
            VendorGpuOcDefaults.FormatSource(source),
            SettingsManager.GetString(VendorGpuOcDefaults.SourceKey(profile), string.Empty));
    }
}
