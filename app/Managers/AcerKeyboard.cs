using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nitrous.Helpers;
using Nitrous.Interop;

namespace Nitrous.Managers;

/// <summary>
/// Lighting effects supported by the keyboard controller.
/// </summary>
public enum KeyboardEffect : byte
{
    Direct = 1,
    Static = 2,
    Breathing = 4,
    Neon = 5,
    Wave = 7,
    Shifting = 8,
    Zoom = 9,
    Meteor = 10,
    Twinkling = 11
}

/// <summary>
/// RGB Color as accepted by the keyboard lighting controller.
/// </summary>
public readonly record struct KeyboardColor(byte R, byte G, byte B)
{
    public static KeyboardColor White { get; } = new(255, 255, 255);

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static KeyboardColor FromHex(string? hex)
    {
        var s = (hex ?? string.Empty).TrimStart('#');
        return s.Length == 6 &&
               uint.TryParse(s, System.Globalization.NumberStyles.AllowHexSpecifier,
                   System.Globalization.CultureInfo.InvariantCulture, out var rgb)
            ? new KeyboardColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : White;
    }
}

/// <summary>
/// Controls four-zone keyboard RGB lighting via the ENEK HID interface (0CF2:5130).
/// Feature report 0xA4, 11 bytes.
/// </summary>
internal static class AcerKeyboard
{
    public const ushort VendorId = 0x0CF2;
    public const ushort ProductId = 0x5130;
    public const ushort UsagePage = 0xFF5A;
    public const ushort Usage = 0x0001;

    public const int ZoneCount = 4;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;

    private static bool? _hidAvailableForTesting;
    private static bool? _wmiRgbForTesting;

    internal static void SetHidAvailabilityForTesting(bool? available) => _hidAvailableForTesting = available;

    internal static void SetWmiRgbAvailabilityForTesting(bool? available) => _wmiRgbForTesting = available;

    /// <summary>True when the ENE HID controller is present (HID effects + direct zone packets).</summary>
    internal static bool SupportsEffects => IsHidAvailable();

    /// <summary>
    /// True when either lighting channel can drive the keyboard: the ENE HID controller,
    /// or the AcerGamingFunction WMI methods used by WMI-only 4-zone keyboards (AN517-42).
    /// </summary>
    public static bool IsAvailable() => IsHidAvailable() || IsWmiRgbSupported();

    private static bool IsHidAvailable()
    {
        if (_hidAvailableForTesting.HasValue) return _hidAvailableForTesting.Value;

        using var handle = OpenKeyboardDevice();
        return handle != null && !handle.IsInvalid;
    }

    private static bool IsWmiRgbSupported()
    {
        if (_wmiRgbForTesting.HasValue) return _wmiRgbForTesting.Value;

        return AcerCapabilitiesProvider.Current.Smbios.KeyboardColor != 1 &&
               AcerCapabilitiesProvider.Current.Wmi.GamingMethods.Contains("SetGamingRgbKb");
    }

    public static bool SetZoneColors(IReadOnlyList<KeyboardColor> colors, int brightness)
    {
        if (_hidAvailableForTesting == false) return ApplyWmiZoneColors(colors);

        using var handle = OpenKeyboardDevice();
        if (handle == null || handle.IsInvalid) return ApplyWmiZoneColors(colors);

        bool sent = false;
        for (int zone = 0; zone < ZoneCount; zone++)
        {
            var color = zone < colors.Count ? colors[zone] : KeyboardColor.White;
            byte[] packet = KeyboardZoneCodec.BuildZonePacket(zone, color, brightness);
            sent |= Hid.HidD_SetFeature(handle, packet, packet.Length);
        }

        return sent;
    }

    /// <summary>
    /// Writes zone colors over WMI, but only on keyboards whose capability probe reports
    /// 4-zone RGB with <c>SetGamingRgbKb</c>; other models must not receive these payloads.
    /// </summary>
    private static bool ApplyWmiZoneColors(IReadOnlyList<KeyboardColor> colors) =>
        IsWmiRgbSupported() && AcerWmiManager.SetKeyboardZoneColors(colors);

    public static bool SetEffect(
        KeyboardEffect effect, int brightness, int speed, bool leftwards, KeyboardColor color)
    {
        using var handle = OpenKeyboardDevice();
        if (handle == null || handle.IsInvalid) return false;

        byte[] packet = KeyboardZoneCodec.BuildEffectPacket(effect, brightness, speed, leftwards, color);
        return Hid.HidD_SetFeature(handle, packet, packet.Length);
    }

    private static SafeFileHandle? OpenKeyboardDevice()
    {
        try
        {
            foreach (var path in GetHidDevicePaths())
            {
                var handle = Kernel32.CreateFileW(
                    path,
                    GenericRead | GenericWrite,
                    ShareReadWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    continue;
                }

                if (MatchesDevice(handle, VendorId, ProductId, UsagePage, Usage))
                {
                    return handle;
                }

                handle.Dispose();
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"OpenKeyboardDevice failed: {ex.Message}", "AcerKeyboard");
        }

        return null;
    }

    private static bool MatchesDevice(SafeFileHandle handle, ushort vid, ushort pid, ushort usagePage, ushort usage)
    {
        var attributes = new Hid.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Hid.HIDD_ATTRIBUTES>() };
        if (!Hid.HidD_GetAttributes(handle, ref attributes)) return false;
        if (attributes.VendorId != vid || attributes.ProductId != pid) return false;

        if (!Hid.HidD_GetPreparsedData(handle, out var preparsed)) return false;
        try
        {
            var caps = new Hid.HIDP_CAPS();
            if (Hid.HidP_GetCaps(preparsed, ref caps) < 0) return false;
            return caps.UsagePage == usagePage && caps.Usage == usage;
        }
        finally
        {
            Hid.HidD_FreePreparsedData(preparsed);
        }
    }

    private static IReadOnlyList<string> GetHidDevicePaths()
    {
        Hid.HidD_GetHidGuid(out var guid);
        if (Cfgmgr32.CM_Get_Device_Interface_List_SizeW(out int length, ref guid, null, 0) != 0 || length <= 0)
            return Array.Empty<string>();

        var buffer = new char[length];
        if (Cfgmgr32.CM_Get_Device_Interface_ListW(ref guid, null, buffer, length, 0) != 0)
            return Array.Empty<string>();

        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
