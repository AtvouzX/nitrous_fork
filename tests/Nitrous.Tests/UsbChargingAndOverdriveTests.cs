using System;
using System.Threading;
using System.Windows;
using Nitrous.Managers;
using Nitrous.Ui.Views;
using Xunit;
using Application = System.Windows.Application;

namespace Nitrous.Tests;

public class UsbChargingAndOverdriveTests : IDisposable
{
    public UsbChargingAndOverdriveTests()
    {
        AcerWmiManager.SetGamingProfileReaderForTesting(null);
        AcerWmiManager.SetUsbChargingReaderForTesting(null);
    }

    public void Dispose()
    {
        AcerWmiManager.SetGamingProfileReaderForTesting(null);
        AcerWmiManager.SetUsbChargingReaderForTesting(null);
    }

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
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0x10)]
    public void GetUsbCharging_WhenStateUnsupported_ReturnsNull(byte state)
    {
        ulong raw = ((ulong)state << 8) | 0x04UL;
        AcerWmiManager.SetUsbChargingReaderForTesting(() => raw);

        var result = AcerWmiManager.GetUsbCharging();
        Assert.Null(result);
    }

    [Theory]
    [InlineData(AcerWmiProtocol.UsbChargingOn, 10, true, 10)]
    [InlineData(AcerWmiProtocol.UsbChargingOn, 20, true, 20)]
    [InlineData(AcerWmiProtocol.UsbChargingOn, 30, true, 30)]
    [InlineData(AcerWmiProtocol.UsbChargingOff, 20, false, 20)]
    [InlineData(AcerWmiProtocol.UsbChargingOn, 50, true, null)] // Unsupported floor
    public void GetUsbCharging_DecodesStateAndFloorCorrectly(byte state, int floor, bool expectedOn, int? expectedFloor)
    {
        ulong raw = 0x04UL | ((ulong)state << 8) | ((ulong)floor << 16);
        AcerWmiManager.SetUsbChargingReaderForTesting(() => raw);

        var result = AcerWmiManager.GetUsbCharging();
        Assert.NotNull(result);
        Assert.Equal(expectedOn, result.Value.On);
        Assert.Equal(expectedFloor, result.Value.Floor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(50)]
    [InlineData(-5)]
    public void SetUsbCharging_RejectsInvalidFloor_ReturnsFalse(int invalidFloor)
    {
        bool success = AcerWmiManager.SetUsbCharging(true, invalidFloor);
        Assert.False(success);
    }

    [Theory]
    [InlineData(0xFF, false, false)]
    [InlineData(0x01, true, true)]
    [InlineData(0x00, true, false)]
    public void GetLcdOverdrive_DecodesSupportAndStateCorrectly(byte byte6, bool expectedSupported, bool expectedOn)
    {
        ulong raw = 0x10UL | ((ulong)byte6 << 48);
        AcerWmiManager.SetGamingProfileReaderForTesting(() => raw);

        var result = AcerWmiManager.GetLcdOverdrive();
        Assert.NotNull(result);
        Assert.Equal(expectedSupported, result.Value.Supported);
        Assert.Equal(expectedOn, result.Value.On);
    }

    [Fact]
    public void DashboardPageView_WhenOverdriveUnsupported_HidesRow()
    {
        RunInSta(() =>
        {
            AcerWmiManager.SetGamingProfileReaderForTesting(() => 0x10UL | (0xFFUL << 48));

            var page = new DashboardPageView();
            var row = (FrameworkElement)page.FindName("LcdOverdriveRow");

            Assert.NotNull(row);
            Assert.Equal(Visibility.Collapsed, row.Visibility);
        });
    }

    [Fact]
    public void DashboardPageView_WhenOverdriveSupported_ShrinksTelemetryToFitPage()
    {
        RunInSta(() =>
        {
            AcerWmiManager.SetGamingProfileReaderForTesting(() => 0x10UL | (0x01UL << 48));

            var page = new DashboardPageView();
            var telemetry = (FrameworkElement)page.FindName("TelemetryGrid");

            Assert.NotNull(telemetry);
            Assert.Equal(136.0, telemetry.Height);
        });
    }

    [Fact]
    public void DashboardPageView_WhenOverdriveSupportedAndOn_ShowsRowAndChecksToggle()
    {
        RunInSta(() =>
        {
            AcerWmiManager.SetGamingProfileReaderForTesting(() => 0x10UL | (0x01UL << 48));

            var page = new DashboardPageView();
            var row = (FrameworkElement)page.FindName("LcdOverdriveRow");
            var toggle = (System.Windows.Controls.CheckBox)page.FindName("TogLcdOverdrive");

            Assert.NotNull(row);
            Assert.NotNull(toggle);
            Assert.Equal(Visibility.Visible, row.Visibility);
            Assert.True(toggle.IsChecked);
        });
    }
}
