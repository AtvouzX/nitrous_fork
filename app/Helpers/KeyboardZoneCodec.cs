using System;
using Nitrous.Managers;

namespace Nitrous.Helpers;

public static class KeyboardZoneCodec
{
    public const byte ReportId = 0xA4;
    public const byte KeyboardDevice = 0x21;
    public const byte AllZones = 0x0F;
    public const int PacketLength = 11;
    public const int ZoneCount = 4;
    public const int MinSpeed = 1;
    public const int MaxSpeed = 9;

    /// <summary>SetGamingLEDBehavior payload that enables all four zones (NitroSense V3).</summary>
    public const ulong AllZonesEnabledBehavior = 0x08ul | ((ulong)AllZones << 40);

    public static byte[] BuildZonePacket(int zoneIndex, KeyboardColor color, int brightness)
    {
        if (zoneIndex < 0 || zoneIndex >= ZoneCount)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex), $"Zone index must be 0..{ZoneCount - 1}, got {zoneIndex}");

        byte level = (byte)Math.Clamp(brightness, 0, 100);
        var packet = new byte[PacketLength];
        packet[0] = ReportId;
        packet[1] = KeyboardDevice;
        packet[2] = (byte)KeyboardEffect.Static;
        packet[3] = level;
        packet[4] = 0; // speed: unused by Static
        packet[5] = 1; // direction: right
        packet[6] = color.R;
        packet[7] = color.G;
        packet[8] = color.B;
        packet[9] = (byte)(1 << zoneIndex);
        packet[10] = 0;
        return packet;
    }

    public static byte[] BuildEffectPacket(
        KeyboardEffect effect, int brightness, int speed, bool leftwards, KeyboardColor color)
    {
        var packet = new byte[PacketLength];
        packet[0] = ReportId;
        packet[1] = KeyboardDevice;
        packet[2] = (byte)effect;
        packet[3] = (byte)Math.Clamp(brightness, 0, 100);
        packet[4] = (byte)Math.Clamp(speed, MinSpeed, MaxSpeed);
        packet[5] = (byte)(leftwards ? 2 : 1);
        packet[6] = color.R;
        packet[7] = color.G;
        packet[8] = color.B;
        packet[9] = AllZones;
        packet[10] = 0;
        return packet;
    }

    /// <summary>
    /// Encodes AcerGamingFunction.SetGamingRgbKb 64-bit gmInput for one keyboard zone,
    /// per NitroSense V3 Change_Zone_Color:
    /// Byte 0: zone mask (1, 2, 4, 8); Byte 1..3: R, G, B.
    /// </summary>
    public static ulong BuildWmiZoneColorPayload(int zoneIndex, KeyboardColor color)
    {
        if (zoneIndex < 0 || zoneIndex >= ZoneCount)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex), $"Zone index must be 0..{ZoneCount - 1}, got {zoneIndex}");

        return (1ul << zoneIndex) | ((ulong)color.R << 8) | ((ulong)color.G << 16) | ((ulong)color.B << 24);
    }

    /// <summary>
    /// Encodes AcerGamingFunction.SetGamingLEDBehavior 64-bit gmInput that enables all four zones
    /// (NitroSense V3 Change_Zone_Status: byte 0 = 0x08, zone enables at bits 40..43).
    /// </summary>
    public static ulong BuildWmiZoneEnablePayload() => AllZonesEnabledBehavior;

    public static uint UpdateStickyKeysFlags(uint currentFlags, bool enable)
    {
        const uint skfStickyKeysOn = 0x00000001u;
        return enable ? (currentFlags | skfStickyKeysOn) : (currentFlags & ~skfStickyKeysOn);
    }
}
