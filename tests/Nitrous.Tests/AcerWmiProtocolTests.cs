using System;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class AcerWmiProtocolTests
{
    [Fact]
    public void LcdOverdrive_PayloadAndDecode_MatchesSpec()
    {
        ulong onPayload = AcerWmiProtocol.LcdOverdrivePayload(true);
        ulong offPayload = AcerWmiProtocol.LcdOverdrivePayload(false);

        Assert.Equal(0x10UL | (1UL << 48), onPayload);
        Assert.Equal(0x10UL, offPayload);

        Assert.True(AcerWmiProtocol.LcdOverdriveOn(onPayload));
        Assert.False(AcerWmiProtocol.LcdOverdriveOn(offPayload));

        Assert.True(AcerWmiProtocol.LcdOverdriveSupported(onPayload));
        Assert.True(AcerWmiProtocol.LcdOverdriveSupported(offPayload));
        Assert.False(AcerWmiProtocol.LcdOverdriveSupported(0xFFUL << 48));
    }

    [Theory]
    [InlineData(true, 10)]
    [InlineData(false, 10)]
    [InlineData(true, 20)]
    [InlineData(false, 20)]
    [InlineData(true, 30)]
    [InlineData(false, 30)]
    public void UsbCharging_ValidFloors_ConstructsExpectedPayload(bool on, int floor)
    {
        ulong payload = AcerWmiProtocol.UsbChargingPayload(on, floor);

        byte query = (byte)(payload & 0xFF);
        byte state = (byte)((payload >> 8) & 0xFF);
        byte payloadFloor = (byte)((payload >> 16) & 0xFF);

        Assert.Equal(AcerWmiProtocol.UsbChargingQuery, query);
        Assert.Equal(on ? AcerWmiProtocol.UsbChargingOn : AcerWmiProtocol.UsbChargingOff, state);
        Assert.Equal(floor, payloadFloor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(-10)]
    public void UsbCharging_InvalidFloor_ThrowsArgumentOutOfRangeException(int floor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerWmiProtocol.UsbChargingPayload(true, floor));
    }

    [Theory]
    [InlineData(PowerProfile.Quiet)]
    [InlineData(PowerProfile.Balanced)]
    [InlineData(PowerProfile.Performance)]
    [InlineData(PowerProfile.Turbo)]
    public void PowerMode_Payload_EncodesProfileAndOpcode(PowerProfile profile)
    {
        ulong payload = AcerWmiProtocol.PowerModePayload(profile);

        byte opcode = (byte)(payload & 0xFF);
        byte encodedProfile = (byte)((payload >> 8) & 0xFF);

        Assert.Equal(AcerWmiProtocol.PowerModeOpcode, opcode);
        Assert.Equal((byte)profile, encodedProfile);
    }

    [Theory]
    [InlineData(AcerWmiProtocol.CpuFanDevice, 0, 0)]
    [InlineData(AcerWmiProtocol.CpuFanDevice, 50, 50)]
    [InlineData(AcerWmiProtocol.CpuFanDevice, 100, 100)]
    [InlineData(AcerWmiProtocol.CpuFanDevice, -5, 0)]
    [InlineData(AcerWmiProtocol.CpuFanDevice, 150, 100)]
    [InlineData(AcerWmiProtocol.GpuFanDeviceA, 75, 75)]
    [InlineData(AcerWmiProtocol.GpuFanDeviceB, 80, 80)]
    public void FanSpeed_Payload_ClampsAndEncodesDeviceAndPercent(ulong deviceMask, int inputPercent, int expectedPercent)
    {
        ulong payload = AcerWmiProtocol.FanSpeedPayload(deviceMask, inputPercent);

        ulong device = payload & 0xFF;
        int percent = (int)((payload >> 8) & 0xFF);

        Assert.Equal(deviceMask, device);
        Assert.Equal(expectedPercent, percent);
    }
}
