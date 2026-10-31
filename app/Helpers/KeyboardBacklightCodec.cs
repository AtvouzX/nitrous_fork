using System;
using Nitrous.Enums;

namespace Nitrous.Helpers;

public static class KeyboardBacklightCodec
{
    public const int PayloadLength = 16;
    public const int TimeoutReportLength = 8;
    public const byte ReportIdTimeout = 0x30;
    public const byte SubCommandTimeout = 0x01;
    public const byte ChecksumBase = 0xCE;

    public const ulong GamingProfileTimeoutEnabled = 0x01000002ul;
    public const ulong GamingProfileTimeoutDisabled = 0x00000002ul;

    public const int DefaultBkHotkeyNumber = 132; // 0x84 on Nitro ANV14-41

    // EC brightness inverted on ANV14-41 (hardware probe): 50 = bright, 100 = dim.
    public static int EncodeBrightnessPercentage(KeyboardBacklightLevel level) => level switch
    {
        KeyboardBacklightLevel.Off => 0,
        KeyboardBacklightLevel.Level1 => 100,
        KeyboardBacklightLevel.Level2 => 50,
        _ => 0,
    };

    public static KeyboardBacklightLevel DecodeBrightnessLevel(int percentage)
    {
        if (percentage <= 0) return KeyboardBacklightLevel.Off;
        if (percentage <= 50) return KeyboardBacklightLevel.Level2;
        return KeyboardBacklightLevel.Level1;
    }

    /// <summary>
    /// Encodes the 16-byte gmInput buffer for AcerGamingFunction.SetGamingKBBacklight
    /// per AcerLightingService.exe (ECKeyboard::SetGamingKbbacklightFunction at RVA 0xB270).
    /// Byte 0: Mode (0x00 = Static)
    /// Byte 1: 0x04 (EC expects; echoed by GetGamingKBBacklight)
    /// Byte 2: Brightness (0 = off/dead on ANV14-41, 100 = Level1 dim, 50 = Level2 bright)
    /// Byte 3: Reserved/Zero (0x00)
    /// Byte 4: 0x04 (EC expects; echoed by GetGamingKBBacklight)
    /// Byte 5..7: R, G, B
    /// Byte 8: Fixed marker 0x03
    /// Byte 9: Device index 0x01 (both 0x01 and 0x03 latch to keypress on ANV14-41; no instant-apply index found)
    /// </summary>
    public static byte[] EncodeBacklightPayload(
        KeyboardBacklightLevel level,
        ReadOnlySpan<byte> existingPayload = default,
        bool isSingleColor = true)
    {
        byte[] payload = new byte[PayloadLength];

        if (!existingPayload.IsEmpty)
        {
            int copyLen = Math.Min(existingPayload.Length, PayloadLength);
            existingPayload[..copyLen].CopyTo(payload);
        }
        else if (!isSingleColor)
        {
            payload[5] = 0xFF;
            payload[6] = 0xFF;
            payload[7] = 0xFF;
        }

        // Byte 0: Mode (0x00 = Static)
        payload[0] = 0x00;

        // Byte 1: EC header (verified Probe2 echo: GetGamingKBBacklight returns 0x04)
        payload[1] = 0x04;

        // Byte 2: Brightness (100 = dim, 50 = bright per EC probe)
        payload[2] = (byte)EncodeBrightnessPercentage(level);

        // Byte 3: Must be 0
        payload[3] = 0x00;

        // Byte 4: EC header (verified Probe2 echo: GetGamingKBBacklight returns 0x04)
        payload[4] = 0x04;

        // Byte 8: Fixed Acer EC hardware marker
        payload[8] = 0x03;

        // Byte 9: Device index 1 (latches to keypress; no instant-apply index exists)
        payload[9] = 0x01;

        return payload;
    }

    public static KeyboardBacklightLevel DecodeBacklightLevelFromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3) return KeyboardBacklightLevel.Off;
        return DecodeBrightnessLevel(payload[2]);
    }

    /// <summary>
    /// Encodes AcerGamingFunction.SetGamingProfile 64-bit input for 30s keyboard backlight timeout
    /// per AcerAgentService.exe (RVA 0x3E1D6).
    /// </summary>
    public static ulong EncodeGamingProfileTimeout(bool enabled) =>
        enabled ? GamingProfileTimeoutEnabled : GamingProfileTimeoutDisabled;

    /// <summary>
    /// Encodes the 16-byte gmInput buffer for AcerGamingFunction.SetGamingLED
    /// on Single-Color EC keyboards (Probe2: buf[0]=0x08, buf[5]=0x0F on / 0x00 off).
    /// Scalar ulong input throws InvalidCastException — must use buffer invoke.
    /// </summary>
    public static byte[] EncodeSingleColorGamingLedPayload(KeyboardBacklightLevel level)
    {
        byte[] payload = new byte[PayloadLength];
        payload[0] = 0x08;
        payload[5] = level == KeyboardBacklightLevel.Off ? (byte)0x00 : (byte)0x0F;
        return payload;
    }

    /// <summary>
    /// Encodes APGeAction.GetFunction 32-bit uiInput for reading keyboard backlight state
    /// per AcerAgentService.exe (RVA 0x3E062).
    /// </summary>
    public static uint EncodeApGeActionGetBacklight(int hotkeyNumber = DefaultBkHotkeyNumber) =>
        ((uint)(hotkeyNumber & 0xFF) << 8) | 0x00080001u;

    /// <summary>
    /// Encodes APGeAction.SetFunction 64-bit uiInput for setting keyboard backlight brightness and 30s timeout
    /// per AcerAgentService.exe (RVA 0x3E07A..0x3E0B0).
    /// </summary>
    public static ulong EncodeApGeActionSetBacklight(
        KeyboardBacklightLevel level,
        bool timeoutEnabled,
        int hotkeyNumber = DefaultBkHotkeyNumber)
    {
        ulong brightness = (ulong)EncodeBrightnessPercentage(level);
        ulong timeoutMask = timeoutEnabled ? 0x1E00ul : 0x0000ul;
        ulong highDword = (timeoutMask | brightness) << 32;
        ulong lowDword = (((ulong)(hotkeyNumber & 0xFF)) << 8) | 0x00080002ul;
        return highDword | lowDword;
    }

    public static byte[] EncodeTimeoutReport(bool enabled)
    {
        byte state = enabled ? (byte)0x01 : (byte)0x00;
        return
        [
            ReportIdTimeout,
            SubCommandTimeout,
            state,
            0x00,
            0x00,
            0x00,
            0x00,
            (byte)(ChecksumBase - state)
        ];
    }

    public static bool TryDecodeTimeoutReport(ReadOnlySpan<byte> report, out bool enabled)
    {
        enabled = false;
        if (report.Length != TimeoutReportLength) return false;
        if (report[0] != ReportIdTimeout || report[1] != SubCommandTimeout) return false;

        byte state = report[2];
        byte expectedChecksum = (byte)(ChecksumBase - state);
        if (report[7] != expectedChecksum) return false;

        enabled = state == 0x01;
        return true;
    }
}
