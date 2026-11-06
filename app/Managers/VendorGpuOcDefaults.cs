using System;
using System.Collections.Generic;
using Nitrous.Enums;

namespace Nitrous.Managers;

/// <summary>
/// Who last wrote a profile's GPU offsets. Value comparison cannot distinguish
/// "never set" from "user explicitly chose 0/0", so authorship is tracked.
/// </summary>
public enum VendorGpuOcSource
{
    Default,
    Vendor,
    Custom
}

public enum VendorGpuOcToggleAction
{
    None,
    SeedVendor,
    ResetStock
}

public readonly record struct VendorGpuOcPlan(
    VendorGpuOcToggleAction Action,
    int Core,
    int Memory,
    VendorGpuOcSource ResultSource);

/// <summary>
/// Pure vendor GPU offset tables reverse engineered from the NitroSense 5.1.385
/// package (<c>AgentService\driver\*.ini</c>). 140 distinct PCI IDs collapse to
/// five offset tables. Keyed on PCI device + subsystem ID, never on model name:
/// one chassis may ship several GPU variants.
///
/// Vendor profile names map onto Nitrous profiles:
/// Default -> Balanced, Extreme -> Performance. A missing Turbo key inherits
/// Performance, so every table below is fully resolved.
/// </summary>
public static class VendorGpuOcDefaults
{
    public const string ToggleSettingKey = "UseVendorGpuDefaults";

    // Per table: Quiet, Balanced, Performance, Turbo. Core/Memory MHz.
    private static readonly (int Core, int Memory)[][] Tables =
    [
        [(0, 0), (0, 0), (0, 0), (100, 200)],
        [(0, 0), (0, 0), (100, 200), (100, 200)],
        [(0, 0), (0, 0), (0, 0), (100, 0)],
        [(0, 0), (0, 0), (50, 0), (100, 0)],
        [(0, 0), (0, 0), (100, 0), (100, 0)]
    ];

    private static readonly Dictionary<string, int> PciIdTable = new(StringComparer.Ordinal)
    {
        // Table 0: Quiet 0/0, Balanced 0/0, Performance 0/0, Turbo +100/+200 (55 ids)
        ["DEV_25AC&SUBSYS_16861025"] = 0,
        ["DEV_25AC&SUBSYS_16871025"] = 0,
        ["DEV_25AC&SUBSYS_168A1025"] = 0,
        ["DEV_25AC&SUBSYS_17451025"] = 0,
        ["DEV_25AC&SUBSYS_17461025"] = 0,
        ["DEV_25AC&SUBSYS_18371025"] = 0,
        ["DEV_25AC&SUBSYS_183B1025"] = 0,
        ["DEV_25AC&SUBSYS_19091025"] = 0,
        ["DEV_25AC&SUBSYS_190A1025"] = 0,
        ["DEV_25AD&SUBSYS_172C1025"] = 0,
        ["DEV_25AD&SUBSYS_181A1025"] = 0,
        ["DEV_2757&SUBSYS_172D1025"] = 0,
        ["DEV_2757&SUBSYS_17361025"] = 0,
        ["DEV_27E0&SUBSYS_166C1025"] = 0,
        ["DEV_27E0&SUBSYS_166E1025"] = 0,
        ["DEV_27E0&SUBSYS_16811025"] = 0,
        ["DEV_27E0&SUBSYS_16821025"] = 0,
        ["DEV_27E0&SUBSYS_172D1025"] = 0,
        ["DEV_27E0&SUBSYS_17361025"] = 0,
        ["DEV_2860&SUBSYS_166C1025"] = 0,
        ["DEV_2860&SUBSYS_166E1025"] = 0,
        ["DEV_2860&SUBSYS_16811025"] = 0,
        ["DEV_2860&SUBSYS_16821025"] = 0,
        ["DEV_2860&SUBSYS_170F1025"] = 0,
        ["DEV_2860&SUBSYS_17301025"] = 0,
        ["DEV_2860&SUBSYS_17311025"] = 0,
        ["DEV_28A0&SUBSYS_170F1025"] = 0,
        ["DEV_28A0&SUBSYS_172C1025"] = 0,
        ["DEV_28A0&SUBSYS_181A1025"] = 0,
        ["DEV_28A1&SUBSYS_16861025"] = 0,
        ["DEV_28A1&SUBSYS_16871025"] = 0,
        ["DEV_28A1&SUBSYS_168A1025"] = 0,
        ["DEV_28A1&SUBSYS_170F1025"] = 0,
        ["DEV_28A1&SUBSYS_172C1025"] = 0,
        ["DEV_28A1&SUBSYS_181A1025"] = 0,
        ["DEV_28A1&SUBSYS_18371025"] = 0,
        ["DEV_28A1&SUBSYS_183B1025"] = 0,
        ["DEV_28A1&SUBSYS_19091025"] = 0,
        ["DEV_28A1&SUBSYS_190A1025"] = 0,
        ["DEV_28E0&SUBSYS_166C1025"] = 0,
        ["DEV_28E0&SUBSYS_166E1025"] = 0,
        ["DEV_28E0&SUBSYS_16741025"] = 0,
        ["DEV_28E0&SUBSYS_16751025"] = 0,
        ["DEV_28E0&SUBSYS_16781025"] = 0,
        ["DEV_28E0&SUBSYS_16811025"] = 0,
        ["DEV_28E0&SUBSYS_16821025"] = 0,
        ["DEV_28E0&SUBSYS_170F1025"] = 0,
        ["DEV_28E0&SUBSYS_17301025"] = 0,
        ["DEV_28E0&SUBSYS_17311025"] = 0,
        ["DEV_28E1&SUBSYS_16741025"] = 0,
        ["DEV_28E1&SUBSYS_16751025"] = 0,
        ["DEV_28E1&SUBSYS_16781025"] = 0,
        ["DEV_28E1&SUBSYS_170F1025"] = 0,
        ["DEV_28E1&SUBSYS_17301025"] = 0,
        ["DEV_28E1&SUBSYS_17311025"] = 0,

        // Table 1: Quiet 0/0, Balanced 0/0, Performance +100/+200, Turbo +100/+200 (30 ids)
        ["DEV_1F9D&SUBSYS_15971025"] = 1,
        ["DEV_1F9D&SUBSYS_159C1025"] = 1,
        ["DEV_249C&SUBSYS_15981025"] = 1,
        ["DEV_249D&SUBSYS_15971025"] = 1,
        ["DEV_249D&SUBSYS_15981025"] = 1,
        ["DEV_249D&SUBSYS_159C1025"] = 1,
        ["DEV_24A0&SUBSYS_15971025"] = 1,
        ["DEV_24A0&SUBSYS_15981025"] = 1,
        ["DEV_24A0&SUBSYS_159C1025"] = 1,
        ["DEV_24A0&SUBSYS_17091025"] = 1,
        ["DEV_24DC&SUBSYS_15991025"] = 1,
        ["DEV_24DD&SUBSYS_15991025"] = 1,
        ["DEV_24E0&SUBSYS_15991025"] = 1,
        ["DEV_2520&SUBSYS_15971025"] = 1,
        ["DEV_2520&SUBSYS_15981025"] = 1,
        ["DEV_2520&SUBSYS_159C1025"] = 1,
        ["DEV_2560&SUBSYS_15991025"] = 1,
        ["DEV_2560&SUBSYS_16461025"] = 1,
        ["DEV_25A0&SUBSYS_15971025"] = 1,
        ["DEV_25A0&SUBSYS_159C1025"] = 1,
        ["DEV_25A2&SUBSYS_15971025"] = 1,
        ["DEV_25A2&SUBSYS_159C1025"] = 1,
        ["DEV_25AD&SUBSYS_171E1025"] = 1,
        ["DEV_25AD&SUBSYS_17581025"] = 1,
        ["DEV_25AD&SUBSYS_18541025"] = 1,
        ["DEV_27A0&SUBSYS_17091025"] = 1,
        ["DEV_28A0&SUBSYS_17581025"] = 1,
        ["DEV_28A1&SUBSYS_171E1025"] = 1,
        ["DEV_28A1&SUBSYS_17581025"] = 1,
        ["DEV_28A1&SUBSYS_18541025"] = 1,

        // Table 2: Quiet 0/0, Balanced 0/0, Performance 0/0, Turbo +100/0 (31 ids)
        ["DEV_25AC&SUBSYS_172C1025"] = 2,
        ["DEV_25AC&SUBSYS_17671025"] = 2,
        ["DEV_25AC&SUBSYS_176C1025"] = 2,
        ["DEV_25AC&SUBSYS_17991025"] = 2,
        ["DEV_25AC&SUBSYS_181A1025"] = 2,
        ["DEV_25AD&SUBSYS_17991025"] = 2,
        ["DEV_2757&SUBSYS_167B1025"] = 2,
        ["DEV_2860&SUBSYS_166D1025"] = 2,
        ["DEV_2860&SUBSYS_166F1025"] = 2,
        ["DEV_2860&SUBSYS_16751025"] = 2,
        ["DEV_2860&SUBSYS_17291025"] = 2,
        ["DEV_2860&SUBSYS_172D1025"] = 2,
        ["DEV_2860&SUBSYS_17361025"] = 2,
        ["DEV_2860&SUBSYS_17491025"] = 2,
        ["DEV_28A1&SUBSYS_17991025"] = 2,
        ["DEV_28E0&SUBSYS_166D1025"] = 2,
        ["DEV_28E0&SUBSYS_166F1025"] = 2,
        ["DEV_28E0&SUBSYS_17291025"] = 2,
        ["DEV_28E0&SUBSYS_17381025"] = 2,
        ["DEV_28E0&SUBSYS_17421025"] = 2,
        ["DEV_28E0&SUBSYS_17491025"] = 2,
        ["DEV_28E0&SUBSYS_17541025"] = 2,
        ["DEV_28E0&SUBSYS_176B1025"] = 2,
        ["DEV_28E1&SUBSYS_166D1025"] = 2,
        ["DEV_28E1&SUBSYS_166F1025"] = 2,
        ["DEV_28E1&SUBSYS_17291025"] = 2,
        ["DEV_28E1&SUBSYS_17381025"] = 2,
        ["DEV_28E1&SUBSYS_17421025"] = 2,
        ["DEV_28E1&SUBSYS_17491025"] = 2,
        ["DEV_28E1&SUBSYS_17541025"] = 2,
        ["DEV_28E1&SUBSYS_176B1025"] = 2,

        // Table 3: Quiet 0/0, Balanced 0/0, Performance +50/0, Turbo +100/0 (19 ids)
        ["DEV_25AC&SUBSYS_17471025"] = 3,
        ["DEV_25AC&SUBSYS_17531025"] = 3,
        ["DEV_25AC&SUBSYS_176E1025"] = 3,
        ["DEV_2860&SUBSYS_16771025"] = 3,
        ["DEV_2860&SUBSYS_16791025"] = 3,
        ["DEV_2860&SUBSYS_17321025"] = 3,
        ["DEV_2860&SUBSYS_17331025"] = 3,
        ["DEV_28A1&SUBSYS_16851025"] = 3,
        ["DEV_28A1&SUBSYS_168B1025"] = 3,
        ["DEV_28E0&SUBSYS_16771025"] = 3,
        ["DEV_28E0&SUBSYS_16791025"] = 3,
        ["DEV_28E0&SUBSYS_17321025"] = 3,
        ["DEV_28E0&SUBSYS_17331025"] = 3,
        ["DEV_28E0&SUBSYS_176D1025"] = 3,
        ["DEV_28E1&SUBSYS_16771025"] = 3,
        ["DEV_28E1&SUBSYS_16791025"] = 3,
        ["DEV_28E1&SUBSYS_17321025"] = 3,
        ["DEV_28E1&SUBSYS_17331025"] = 3,
        ["DEV_28E1&SUBSYS_176D1025"] = 3,

        // Table 4: Quiet 0/0, Balanced 0/0, Performance +100/0, Turbo +100/0 (5 ids)
        ["DEV_25AC&SUBSYS_171E1025"] = 4,
        ["DEV_25AC&SUBSYS_17581025"] = 4,
        ["DEV_25AC&SUBSYS_18541025"] = 4,
        ["DEV_25AC&SUBSYS_18551025"] = 4,
        ["DEV_28A1&SUBSYS_18551025"] = 4
    };

    internal static int TableCount => Tables.Length;

    internal static int KnownPciIdCount => PciIdTable.Count;

    /// <summary>
    /// Normalizes a PNP device ID to <c>DEV_xxxx&amp;SUBSYS_xxxxxxxx</c>.
    /// Drops <c>VEN_</c> and <c>REV_</c>. Returns null on malformed input.
    /// </summary>
    public static string? NormalizePciId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        int devStart = raw.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
        if (devStart < 0 || devStart + 8 > raw.Length) return null;
        if (!IsHex(raw.AsSpan(devStart + 4, 4))) return null;
        if (devStart + 8 < raw.Length && raw[devStart + 8] != '&') return null;

        int subStart = raw.IndexOf("SUBSYS_", StringComparison.OrdinalIgnoreCase);
        if (subStart < 0 || subStart + 15 > raw.Length) return null;
        if (!IsHex(raw.AsSpan(subStart + 7, 8))) return null;
        if (subStart + 15 < raw.Length && raw[subStart + 15] != '&') return null;

        return string.Concat(
            "DEV_",
            raw.AsSpan(devStart + 4, 4),
            "&SUBSYS_",
            raw.AsSpan(subStart + 7, 8)).ToUpperInvariant();
    }

    /// <summary>Table index for a PCI ID, or null when the SKU is not listed.</summary>
    public static int? ResolveTableIndex(string? pciId)
    {
        string? normalized = NormalizePciId(pciId);
        if (normalized == null) return null;
        return PciIdTable.TryGetValue(normalized, out int index) ? index : null;
    }

    /// <summary>Vendor offsets for the SKU. Unlisted or malformed IDs return stock 0/0.</summary>
    public static (int Core, int Memory) GetOffsets(string? pciId, PowerProfile profile)
    {
        int? index = ResolveTableIndex(pciId);
        return index is null ? (0, 0) : GetOffsetsByTableIndex(index.Value, profile);
    }

    internal static (int Core, int Memory) GetOffsetsByTableIndex(int tableIndex, PowerProfile profile)
    {
        if (tableIndex < 0 || tableIndex >= Tables.Length) return (0, 0);

        int row = profile switch
        {
            PowerProfile.Quiet => 0,
            PowerProfile.Balanced => 1,
            PowerProfile.Performance => 2,
            PowerProfile.Turbo => 3,
            _ => 1
        };
        return Tables[tableIndex][row];
    }

    public static string SourceKey(PowerProfile profile) => $"GpuSource_{profile}";

    public static VendorGpuOcSource ParseSource(string? raw) => raw switch
    {
        "Vendor" => VendorGpuOcSource.Vendor,
        "Custom" => VendorGpuOcSource.Custom,
        _ => VendorGpuOcSource.Default
    };

    public static string FormatSource(VendorGpuOcSource source) => source switch
    {
        VendorGpuOcSource.Vendor => "Vendor",
        VendorGpuOcSource.Custom => "Custom",
        _ => "Default"
    };

    /// <summary>
    /// Migration rule for installs predating authorship keys: a non-zero stored
    /// offset must have been a user choice, 0/0 is indistinguishable from stock.
    /// </summary>
    public static VendorGpuOcSource InferLegacySource(int core, int memory) =>
        core != 0 || memory != 0 ? VendorGpuOcSource.Custom : VendorGpuOcSource.Default;

    /// <summary>
    /// Decides what flipping the vendor toggle does to one profile.
    /// Custom profiles are never modified in either direction.
    /// </summary>
    public static VendorGpuOcPlan ResolveTogglePlan(
        VendorGpuOcSource current,
        bool enable,
        int vendorCore,
        int vendorMemory)
    {
        if (current == VendorGpuOcSource.Custom)
        {
            return new VendorGpuOcPlan(VendorGpuOcToggleAction.None, 0, 0, VendorGpuOcSource.Custom);
        }

        if (enable)
        {
            return new VendorGpuOcPlan(
                VendorGpuOcToggleAction.SeedVendor, vendorCore, vendorMemory, VendorGpuOcSource.Vendor);
        }

        return current == VendorGpuOcSource.Vendor
            ? new VendorGpuOcPlan(VendorGpuOcToggleAction.ResetStock, 0, 0, VendorGpuOcSource.Default)
            : new VendorGpuOcPlan(VendorGpuOcToggleAction.None, 0, 0, VendorGpuOcSource.Default);
    }

    /// <summary>
    /// Immediate apply is only safe with the dGPU awake and on AC power.
    /// Otherwise the existing wake auto-apply path picks the change up later.
    /// </summary>
    public static bool AllowsImmediateApply(bool isGpuAwake, bool isAcOnline) => isGpuAwake && isAcOnline;

    /// <summary>One-time tooltip text naming the exact offsets this SKU would receive.</summary>
    public static string DescribeOffsets(string? pciId)
    {
        int? index = ResolveTableIndex(pciId);
        if (index is null)
        {
            return "Applies NitroSense's per-laptop GPU offsets. This GPU is not listed, so every profile stays at stock 0/0 MHz.";
        }

        var quiet = GetOffsetsByTableIndex(index.Value, PowerProfile.Quiet);
        var balanced = GetOffsetsByTableIndex(index.Value, PowerProfile.Balanced);
        var performance = GetOffsetsByTableIndex(index.Value, PowerProfile.Performance);
        var turbo = GetOffsetsByTableIndex(index.Value, PowerProfile.Turbo);

        return $"Applies NitroSense offsets for this GPU: Quiet {Format(quiet)}, Balanced {Format(balanced)}, " +
               $"Performance {Format(performance)}, Turbo {Format(turbo)} MHz. Custom profiles are never overwritten.";
    }

    private static string Format((int Core, int Memory) offsets) =>
        $"{Signed(offsets.Core)}/{Signed(offsets.Memory)}";

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

    private static bool IsHex(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }
}
