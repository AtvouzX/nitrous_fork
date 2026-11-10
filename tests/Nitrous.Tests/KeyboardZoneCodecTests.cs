using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Ui.Views;
using Xunit;
using Application = System.Windows.Application;

namespace Nitrous.Tests;

public class KeyboardZoneCodecTests
{
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                    new Application();
                action();
            }
            catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void BuildZonePacket_ValidZone_ConstructsExpectedReport(int zoneIndex, byte expectedMask)
    {
        var color = new KeyboardColor(255, 128, 64);
        byte[] packet = KeyboardZoneCodec.BuildZonePacket(zoneIndex, color, 80);

        Assert.Equal(11, packet.Length);
        Assert.Equal(KeyboardZoneCodec.ReportId, packet[0]);
        Assert.Equal(KeyboardZoneCodec.KeyboardDevice, packet[1]);
        Assert.Equal((byte)KeyboardEffect.Static, packet[2]);
        Assert.Equal(80, packet[3]); // brightness
        Assert.Equal(255, packet[6]); // R
        Assert.Equal(128, packet[7]); // G
        Assert.Equal(64, packet[8]);  // B
        Assert.Equal(expectedMask, packet[9]); // zone mask
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(10)]
    public void BuildZonePacket_InvalidZone_ThrowsArgumentOutOfRangeException(int zoneIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KeyboardZoneCodec.BuildZonePacket(zoneIndex, KeyboardColor.White, 100));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(50, 50)]
    [InlineData(150, 100)]
    public void BuildZonePacket_ClampsBrightness(int inputBrightness, byte expectedBrightness)
    {
        byte[] packet = KeyboardZoneCodec.BuildZonePacket(0, KeyboardColor.White, inputBrightness);
        Assert.Equal(expectedBrightness, packet[3]);
    }

    [Fact]
    public void BuildEffectPacket_SetsEffectAllZonesAndClampedSpeed()
    {
        var color = new KeyboardColor(10, 20, 30);
        byte[] packet = KeyboardZoneCodec.BuildEffectPacket(KeyboardEffect.Breathing, 75, 5, leftwards: true, color);

        Assert.Equal(11, packet.Length);
        Assert.Equal(KeyboardZoneCodec.ReportId, packet[0]);
        Assert.Equal(KeyboardZoneCodec.KeyboardDevice, packet[1]);
        Assert.Equal((byte)KeyboardEffect.Breathing, packet[2]);
        Assert.Equal(75, packet[3]);
        Assert.Equal(5, packet[4]); // speed
        Assert.Equal(2, packet[5]); // leftwards = 2
        Assert.Equal(10, packet[6]);
        Assert.Equal(20, packet[7]);
        Assert.Equal(30, packet[8]);
        Assert.Equal(KeyboardZoneCodec.AllZones, packet[9]);
    }

    [Theory]
    [InlineData(0x00000000u, true, 0x00000001u)]
    [InlineData(0x00000001u, true, 0x00000001u)]
    [InlineData(0x00000001u, false, 0x00000000u)]
    [InlineData(0x000000FEu, true, 0x000000FFu)]
    [InlineData(0x000000FFu, false, 0x000000FEu)]
    [InlineData(0x12345678u, true, 0x12345679u)]
    [InlineData(0x12345679u, false, 0x12345678u)]
    public void UpdateStickyKeysFlags_PreservesUnrelatedBits(uint currentFlags, bool enable, uint expectedFlags)
    {
        uint updated = KeyboardZoneCodec.UpdateStickyKeysFlags(currentFlags, enable);
        Assert.Equal(expectedFlags, updated);
    }

    [Theory]
    [InlineData("#FF8040", 255, 128, 64)]
    [InlineData("FF8040", 255, 128, 64)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("invalid", 255, 255, 255)]
    [InlineData(null, 255, 255, 255)]
    public void KeyboardColor_FromHex_ParsesOrDefaultsToWhite(string? hex, byte r, byte g, byte b)
    {
        var color = KeyboardColor.FromHex(hex);
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Theory]
    [InlineData(0, 0x01u)]
    [InlineData(1, 0x02u)]
    [InlineData(2, 0x04u)]
    [InlineData(3, 0x08u)]
    public void BuildWmiZoneColorPayload_EncodesZoneMaskAndColor(int zoneIndex, uint expectedMask)
    {
        var color = new KeyboardColor(0x12, 0x34, 0x56);

        ulong payload = KeyboardZoneCodec.BuildWmiZoneColorPayload(zoneIndex, color);

        Assert.Equal(expectedMask | (0x12u << 8) | (0x34u << 16) | (0x56u << 24), (uint)payload);
    }

    [Fact]
    public void BuildWmiZoneEnablePayload_EnablesAllZonesAtByte5()
    {
        ulong payload = KeyboardZoneCodec.BuildWmiZoneEnablePayload();

        Assert.Equal(0x08ul | (0x0Ful << 40), payload);
    }

    [Fact]
    public void AcerKeyboard_SetZoneColors_WithoutHid_FallsBackToWmi()
    {
        var calls = new List<(string Class, string Method, ulong Input)>();
        AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
        {
            calls.Add((cls, method, input));
            return true;
        });
        AcerKeyboard.SetHidAvailabilityForTesting(false);
        AcerKeyboard.SetWmiRgbAvailabilityForTesting(true);

        try
        {
            KeyboardColor[] zones =
            [
                new(255, 0, 0),
                new(0, 255, 0),
                new(0, 0, 255),
                new(255, 255, 255)
            ];

            bool applied = AcerKeyboard.SetZoneColors(zones, 100);

            Assert.True(applied);
            Assert.Equal(4, calls.Count(c => c.Method == "SetGamingRgbKb"));
            Assert.Contains(calls, c => c.Method == "SetGamingRgbKb" && c.Input == (1ul | (255ul << 8)));
            Assert.Contains(calls, c => c.Method == "SetGamingRgbKb" && c.Input == (2ul | (255ul << 16)));
            Assert.Contains(calls, c => c.Method == "SetGamingRgbKb" && c.Input == (4ul | (255ul << 24)));
            Assert.Contains(calls, c => c.Method == "SetGamingLEDBehavior" && c.Input == KeyboardZoneCodec.BuildWmiZoneEnablePayload());
            Assert.False(AcerKeyboard.SupportsEffects);
        }
        finally
        {
            AcerKeyboard.SetHidAvailabilityForTesting(null);
            AcerKeyboard.SetWmiRgbAvailabilityForTesting(null);
            AcerWmiManager.SetWmiInvokeForTesting(null);
        }
    }

    [Fact]
    public void AcerKeyboard_SetZoneColors_WithoutHidOrWmiRgb_DoesNotWriteWmi()
    {
        var calls = new List<(string Class, string Method, ulong Input)>();
        AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
        {
            calls.Add((cls, method, input));
            return true;
        });
        AcerKeyboard.SetHidAvailabilityForTesting(false);
        AcerKeyboard.SetWmiRgbAvailabilityForTesting(false);

        try
        {
            KeyboardColor[] zones = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 255)];

            bool applied = AcerKeyboard.SetZoneColors(zones, 100);

            Assert.False(applied);
            Assert.Empty(calls);
        }
        finally
        {
            AcerKeyboard.SetHidAvailabilityForTesting(null);
            AcerKeyboard.SetWmiRgbAvailabilityForTesting(null);
            AcerWmiManager.SetWmiInvokeForTesting(null);
        }
    }

    [Fact]
    public void AcerKeyboard_IsAvailable_TrueForWmiRgbOnlyModel()
    {
        AcerKeyboard.SetHidAvailabilityForTesting(false);
        AcerKeyboard.SetWmiRgbAvailabilityForTesting(true);

        try
        {
            Assert.True(AcerKeyboard.IsAvailable());
        }
        finally
        {
            AcerKeyboard.SetHidAvailabilityForTesting(null);
            AcerKeyboard.SetWmiRgbAvailabilityForTesting(null);
        }
    }

    [Fact]
    public void KeyboardPageView_InstantiatesAndInitializesComponents()
    {
        RunInSta(() =>
        {
            var page = new KeyboardPageView();
            var toggler = (System.Windows.Controls.CheckBox)page.FindName("TogStickyKeys");
            var swatches = (FrameworkElement)page.FindName("GrdZoneSwatches");

            Assert.NotNull(toggler);
            Assert.NotNull(swatches);
        });
    }
}
