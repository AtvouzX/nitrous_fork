using System;
using System.Collections.Generic;
using System.Linq;
using Nitrous.Enums;

namespace Nitrous.Helpers;

/// <summary>
/// Pure protocol codec for Acer Embedded Controller HID feature reports (VID 0x1025, PID 0x174B).
/// Contains byte serialization and parsing with no unmanaged dependencies.
/// </summary>
public static class AcerEcHidProtocol
{
    public const ushort ExpectedVendorId = 0x1025;
    public const ushort ExpectedProductId = 0x174B;
    public const ushort ExpectedUsagePage = 0xFF05;
    public const ushort ExpectedUsage = 0x0001;

    public const byte ReportId = 0xA0;
    public const ushort RequestHeader = 0xA000;
    public const ushort StatusDone = 0xE000;
    public const ushort StatusUnsupported = 0xE001;
    public const ushort CommandStatus = 0x0000;
    public const ushort CommandMode = 0x0001;
    public const ushort CommandDevice = 0x000A;

    public const byte KeyboardBacklight = 2;
    public const byte FunctionSet = 1;
    public const byte FunctionGet = 2;
    public const int MinimumReportLength = 65;

    public const byte StatusModeCapability = 5;
    public const string MachineLockName = @"Global\ECHIDFeatureVID_1025&PID_174B";

    public readonly record struct Reply(ushort Status, ushort Command, byte[] Data);

    public static byte[] BuildRequest(ushort command, byte function, byte[]? data = null, int length = MinimumReportLength)
    {
        int reportLength = Math.Max(length, MinimumReportLength);
        var report = new byte[reportLength];
        report[0] = ReportId;
        report[1] = (byte)(RequestHeader & 0xFF);
        report[2] = (byte)(RequestHeader >> 8);
        report[3] = (byte)(command & 0xFF);
        report[4] = (byte)(command >> 8);
        report[5] = function;

        if (data != null && data.Length > 0)
        {
            Array.Copy(data, 0, report, 6, Math.Min(data.Length, reportLength - 6));
        }

        return report;
    }

    public static Reply? DecodeReply(byte[]? report)
    {
        if (report == null || report.Length < 6 || report[0] != ReportId)
            return null;

        var data = new byte[report.Length - 1];
        Array.Copy(report, 1, data, 0, data.Length);
        ushort status = Word(data, 0);
        ushort command = Word(data, 2);

        return new Reply(status, command, data);
    }

    public static string? ParseVersion(byte[]? data)
    {
        if (data == null || data.Length < 8) return null;

        var word6 = Word(data, 6);
        if (word6 == 0xFFFF)
            return $"{Byte(data, 4)}.{Byte(data, 5)}";

        return Byte(data, 4) == 0 ? $"{Byte(data, 6)}.{Byte(data, 7)}" : null;
    }

    public static ushort? ParseCapability(byte[]? data, string? version)
    {
        if (data == null || data.Length < 8) return null;

        bool echoesType = version != null && IsVersionAtLeast(version, 0, 6);
        if (!echoesType) return Word(data, 4);

        return Byte(data, 4) == StatusModeCapability ? Word(data, 6) : null;
    }

    public static bool IsVersionAtLeast(string version, int first, int second)
    {
        var parts = version.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var f) || !int.TryParse(parts[1], out var s))
            return false;

        return f > first || (f == first && s >= second);
    }

    public static IReadOnlyList<PowerProfile> ModesFor(ushort? capability) => capability switch
    {
        1 or 2 => new[] { PowerProfile.Balanced },
        3 => new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance },
        4 => new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo },
        5 => new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo },
        6 => new[] { PowerProfile.Eco, PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo },
        _ => Array.Empty<PowerProfile>()
    };

    public static byte? ValueFor(IReadOnlyList<PowerProfile> modes, PowerProfile mode)
    {
        if (modes == null || modes.Count == 0) return null;

        int index = -1;
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i] == mode)
            {
                index = i;
                break;
            }
        }

        return index < 0 ? null : (byte)(modes.Count - 1 - index);
    }

    public static PowerProfile? ModeFromValue(IReadOnlyList<PowerProfile> modes, byte value)
    {
        if (modes == null || value >= modes.Count) return null;
        return modes[modes.Count - 1 - value];
    }

    public static byte[] EncodeBacklightTimeout(int brightness, int timeoutSeconds)
    {
        var data = new byte[7];
        data[0] = KeyboardBacklight;
        data[1] = 1;
        data[2] = 0;
        data[3] = (byte)(brightness & 0xFF);
        data[4] = (byte)((brightness >> 8) & 0xFF);
        data[5] = (byte)(timeoutSeconds & 0xFF);
        data[6] = (byte)((timeoutSeconds >> 8) & 0xFF);
        return data;
    }

    public static (int Brightness, int TimeoutSeconds)? DecodeBacklightTimeout(byte[]? data)
    {
        if (data == null || data.Length < 12 || Byte(data, 5) != KeyboardBacklight)
            return null;

        return (Word(data, 8), Word(data, 10));
    }

    public static byte Byte(byte[] data, int offset) =>
        offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

    public static ushort Word(byte[] data, int offset) =>
        offset >= 0 && offset + 1 < data.Length ? (ushort)(data[offset] | (data[offset + 1] << 8)) : (ushort)0;
}
