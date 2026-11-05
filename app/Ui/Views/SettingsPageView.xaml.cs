using System;
using System.Windows;
using System.Windows.Controls;
using Nitrous.Managers;
using UserControl = System.Windows.Controls.UserControl;

namespace Nitrous.Ui.Views;

public partial class SettingsPageView : UserControl
{
    private int _usbChargingFloor = 30;
    private bool _usbChargingAvailable;
    private bool _usbChargingLoading = true;
    private bool _expertModeLoading = true;

    public SettingsPageView()
    {
        InitializeComponent();
        SettingsVersionText.Text = $"Nitrous {UpdateManager.CurrentVersion}";
        InitializeUsbCharging();
        InitializeExpertMode();
    }

    public TextBlock VersionTextElement => SettingsVersionText;

    private void InitializeExpertMode()
    {
        TogExpertMode.IsChecked = SettingsManager.Get("ExpertMode", 0) == 1;
        _expertModeLoading = false;
    }

    private void ExpertMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (_expertModeLoading) return;
        bool on = TogExpertMode.IsChecked == true;
        SettingsManager.Save("ExpertMode", on ? 1 : 0);
        LogManager.Info($"Expert mode {(on ? "enabled" : "disabled")}", "SettingsPage");
    }

    private void InitializeUsbCharging()
    {
        _usbChargingLoading = true;
        UsbChargingFloorBox.Items.Clear();
        foreach (var floor in AcerWmiProtocol.UsbChargingFloors)
        {
            UsbChargingFloorBox.Items.Add($"{floor}%");
        }

        var state = AcerWmiManager.GetUsbCharging();
        _usbChargingAvailable = state != null;

        TogUsbCharging.IsEnabled = _usbChargingAvailable;
        UsbChargingFloorBox.IsEnabled = _usbChargingAvailable;

        if (state is { } current)
        {
            TogUsbCharging.IsChecked = current.On;
            _usbChargingFloor = current.Floor ?? 30;
            UsbChargingFloorBox.SelectedIndex = Array.IndexOf(AcerWmiProtocol.UsbChargingFloors, _usbChargingFloor);
        }
        else
        {
            TogUsbCharging.IsChecked = false;
            UsbChargingFloorBox.SelectedIndex = Array.IndexOf(AcerWmiProtocol.UsbChargingFloors, 30);
            TogUsbCharging.ToolTip = "This laptop firmware does not support power-off USB charging controls via WMI.";
            UsbChargingFloorBox.ToolTip = TogUsbCharging.ToolTip;
        }

        _usbChargingLoading = false;
    }

    private void UsbCharging_Toggled(object sender, RoutedEventArgs e)
    {
        if (_usbChargingLoading || !_usbChargingAvailable) return;
        ApplyUsbCharging();
    }

    private void UsbChargingFloor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_usbChargingLoading || !_usbChargingAvailable) return;
        if (UsbChargingFloorBox.SelectedIndex >= 0)
        {
            _usbChargingFloor = AcerWmiProtocol.UsbChargingFloors[UsbChargingFloorBox.SelectedIndex];
            ApplyUsbCharging();
        }
    }

    private void ApplyUsbCharging()
    {
        bool on = TogUsbCharging.IsChecked == true;
        if (!AcerWmiManager.SetUsbCharging(on, _usbChargingFloor))
        {
            LogManager.Warn("Failed to update USB charging settings", "SettingsPage");
        }
    }
}
