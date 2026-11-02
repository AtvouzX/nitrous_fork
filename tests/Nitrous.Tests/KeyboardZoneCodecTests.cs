using System;
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
