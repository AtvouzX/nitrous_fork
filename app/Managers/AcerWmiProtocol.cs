using System;
using Nitrous.Enums;

namespace Nitrous.Managers;

/// <summary>
/// Undocumented Acer EC/WMI contract constants and payload codecs.
/// All payloads return typed numeric primitives (ulong/uint) to guarantee safe WMI serialization.
/// </summary>
internal static class AcerWmiProtocol
{
    public const string WmiNamespace = @"root\wmi";
    public const string GamingClass = "AcerGamingFunction";
    public const string BatteryClass = "BatteryControl";
    public const string ActionClass = "APGeAction";

    public const string GetGamingMiscSetting = "GetGamingMiscSetting";
    public const string SetGamingMiscSetting = "SetGamingMiscSetting";
    public const string SetGamingFanBehavior = "SetGamingFanBehavior";
    public const string SetGamingFanSpeed = "SetGamingFanSpeed";
    public const string GetGamingSysInfo = "GetGamingSysInfo";
    public const string SetBatteryHealthControl = "SetBatteryHealthControl";

    public const string SetGamingProfile = "SetGamingProfile";
    public const string GetGamingProfile = "GetGamingProfile";
    public const uint ProfileQuery = 0;

    // GetGamingSysInfo addresses.
    public const uint CpuTemperature = 0x0101;
    public const uint CpuFanRpm = 0x0201;
    public const uint GpuFanRpm = 0x0601;
    public const uint GpuTemperaturePrimary = 0x0A01;
    public const uint GpuTemperatureFallbackA = 0x0901;
    public const uint GpuTemperatureFallbackB = 0x0B01;

    public const ulong ByteMask = 0xFF;
    public const ulong WordMask = 0xFFFF;

    // GetGamingMiscSetting(0x0A) returns supported-profiles bitmap.
    public const uint SupportedProfilesSetting = 0x0A;
    public const int TurboProfileBit = 5;

    // SetGamingFanBehavior payloads.
    public const ulong FanBehaviorAuto = 0x41000Ful;
    public const ulong FanBehaviorMax = 0x82000Ful;
    public const ulong FanBehaviorCustom = 0xC3000Ful;

    // SetGamingFanSpeed device masks.
    public const ulong CpuFanDevice = 1;
    public const ulong GpuFanDeviceA = 2;
    public const ulong GpuFanDeviceB = 4;

    public const ulong PowerModeOpcode = 0x0Bul;

    // BatteryControl payload.
    public const byte BatteryNumber = 1;
    public const byte BatteryFunctionMask = 1;

    public static ulong LcdOverdrivePayload(bool on) => 0x10UL | ((on ? 1UL : 0UL) << 48);

    public static bool LcdOverdriveOn(ulong output) => ((output >> 48) & 0xFF) == 1;

    public static bool LcdOverdriveSupported(ulong output) => ((output >> 48) & 0xFF) is 0 or 1;

    // APGeAction: power-off USB charging (function 4).
    public const uint UsbChargingQuery = 0x04;
    public const byte UsbChargingOn = 0x0F;
    public const byte UsbChargingOff = 0x1F;

    public static readonly int[] UsbChargingFloors = { 10, 20, 30 };

    public static ulong UsbChargingPayload(bool on, int floor)
    {
        if (Array.IndexOf(UsbChargingFloors, floor) < 0)
            throw new ArgumentOutOfRangeException(nameof(floor), $"USB charging floor must be 10, 20, or 30%, got {floor}");

        return UsbChargingQuery |
               ((ulong)(on ? UsbChargingOn : UsbChargingOff) << 8) |
               ((ulong)floor << 16);
    }

    public static ulong PowerModePayload(PowerProfile profile) => ((ulong)profile << 8) | PowerModeOpcode;

    public static ulong FanSpeedPayload(ulong deviceMask, int percent)
    {
        int clampedPercent = Math.Clamp(percent, 0, 100);
        return deviceMask | ((ulong)clampedPercent << 8);
    }
}
