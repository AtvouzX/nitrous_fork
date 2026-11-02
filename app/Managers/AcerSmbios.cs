using System;
using System.Collections.Generic;
using System.Linq;
using Nitrous.Interop;

namespace Nitrous.Managers;

public readonly record struct AcerSmbiosRecord(byte Id, ushort Value);

public readonly record struct AcerHotkeyRecord(byte Function, byte Flag, ushort Value);

public enum AcerGamingRecord : byte
{
    KeyboardType = 0x08,
    KeyboardColor = 0x0A,
    CustomBootLogo = 0x0D,
    OperatingModes = 0x0F,
    LightBar = 0x17
}

/// <summary>
/// Acer OEM SMBIOS structures, readable without elevation.
/// Type 0xAC carries gaming interface version; Type 0xAA lists hotkey functions.
/// </summary>
public sealed record AcerSmbios(
    byte? GamingMajor,
    byte? GamingMinor,
    IReadOnlyList<AcerSmbiosRecord> GamingRecords,
    IReadOnlyList<AcerHotkeyRecord> HotkeyFunctions)
{
    private const uint RawSmbios = 0x52534D42; // 'RSMB'

    public static AcerSmbios Empty { get; } = new(null, null, Array.Empty<AcerSmbiosRecord>(), Array.Empty<AcerHotkeyRecord>());

    public double? GamingVersion =>
        GamingMajor is { } major && GamingMinor is { } minor ? major + minor / 100.0 : null;

    public int LedArrayLength => (GamingMajor, GamingMinor) switch
    {
        (> 2, not null) or (2, >= 0x5B) => 16,
        (2, >= 0x56) => 12,
        _ => 8
    };

    public bool HasEcLightBars => Gaming(AcerGamingRecord.LightBar) == 1;

    public bool OperatingModesListed => Gaming(AcerGamingRecord.OperatingModes) == 1;

    public ushort? KeyboardColor => Gaming(AcerGamingRecord.KeyboardColor);

    public ushort? Gaming(AcerGamingRecord record) =>
        GamingRecords.Where(r => r.Id == (byte)record)
            .Select(r => (ushort?)r.Value)
            .FirstOrDefault() is { } value and not 0xFF
            ? value
            : null;

    public byte? BacklightHotkey => HotkeyFunctions
        .Where(r => r.Function is >= 0x81 and <= 0x9F && r.Value == 0x0008)
        .Select(r => (byte?)r.Function)
        .LastOrDefault();

    public bool HasHotkeyFunction(byte function) =>
        HotkeyFunctions.Any(r => r.Function == function);

    public string Describe()
    {
        var version = GamingVersion is { } v ? v.ToString("0.00") : "unknown";
        return $"gaming interface {version}; " +
               $"operating modes {(OperatingModesListed ? "listed" : "not listed")}; " +
               $"keyboard colour {KeyboardColor?.ToString() ?? "unknown"}; " +
               $"ec light bars {(HasEcLightBars ? "yes" : "no")}; " +
               $"hotkey functions {HotkeyFunctions.Count}";
    }

    public static AcerSmbios Read()
    {
        try
        {
            var size = Kernel32.GetSystemFirmwareTable(RawSmbios, 0, null, 0);
            if (size == 0 || size > 1024 * 1024) return Empty;

            var buffer = new byte[size];
            if (Kernel32.GetSystemFirmwareTable(RawSmbios, 0, buffer, size) != size) return Empty;

            if (buffer.Length < 8) return Empty;
            var length = (int)Math.Min(BitConverter.ToUInt32(buffer, 4), (uint)(buffer.Length - 8));
            return Parse(buffer.AsSpan(8, length));
        }
        catch (Exception ex)
        {
            LogManager.Debug($"SMBIOS read failed: {ex.Message}", "AcerSmbios");
            return Empty;
        }
    }

    internal static AcerSmbios Parse(ReadOnlySpan<byte> table)
    {
        byte? major = null, minor = null;
        var gaming = new List<AcerSmbiosRecord>();
        var hotkeys = new List<AcerHotkeyRecord>();

        var offset = 0;
        while (offset + 4 <= table.Length)
        {
            var type = table[offset];
            var formattedLength = table[offset + 1];
            if (formattedLength < 4 || offset + formattedLength > table.Length) break;

            var formatted = table.Slice(offset, formattedLength);

            if (type == 0xAC && formatted.Length >= 6)
            {
                major = formatted[4];
                minor = formatted[5];
                for (var i = 6; i + 3 <= formatted.Length; i += 3)
                    gaming.Add(new AcerSmbiosRecord(formatted[i], (ushort)(formatted[i + 1] | (formatted[i + 2] << 8))));
            }
            else if (type == 0xAA && formatted.Length >= 18)
            {
                for (var i = 14; i + 4 <= formatted.Length; i += 4)
                    hotkeys.Add(new AcerHotkeyRecord(
                        formatted[i], formatted[i + 1], (ushort)(formatted[i + 2] | (formatted[i + 3] << 8))));
            }

            if (type == 0x7F) break;

            var next = offset + formattedLength;
            while (next + 1 < table.Length && !(table[next] == 0 && table[next + 1] == 0)) next++;
            offset = next + 2;
        }

        return new AcerSmbios(major, minor, gaming, hotkeys);
    }
}
