using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Ui.Controls;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace Nitrous.Ui.Views;

public partial class KeyboardPageView : UserControl
{
    private bool _isUpdatingUi;

    public KeyboardPageView()
    {
        InitializeComponent();
        RefreshHotkeys();
        RefreshBacklightUi();
    }

    public void RefreshBacklightUi()
    {
        _isUpdatingUi = true;
        try
        {
            var level = AcerWmiManager.GetBacklightLevel() ?? KeyboardBacklightLevel.Level2;
            UpdateLevelButtons(level);

            if (ChkBacklightTimeout != null)
            {
                ChkBacklightTimeout.IsChecked = AcerWmiManager.GetBacklightTimeout();
            }
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void UpdateLevelButtons(KeyboardBacklightLevel activeLevel)
    {
        if (BtnBacklightOff == null || BtnBacklightL1 == null || BtnBacklightL2 == null) return;

        BtnBacklightOff.Variant = activeLevel == KeyboardBacklightLevel.Off ? ChamferButtonVariant.Primary : ChamferButtonVariant.Secondary;
        BtnBacklightL1.Variant = activeLevel == KeyboardBacklightLevel.Level1 ? ChamferButtonVariant.Primary : ChamferButtonVariant.Secondary;
        BtnBacklightL2.Variant = activeLevel == KeyboardBacklightLevel.Level2 ? ChamferButtonVariant.Primary : ChamferButtonVariant.Secondary;

        bool enabled = activeLevel != KeyboardBacklightLevel.Off;
        if (ChkBacklightTimeout != null)
        {
            ChkBacklightTimeout.IsEnabled = enabled;
            ChkBacklightTimeout.Opacity = enabled ? 1.0 : 0.4;
        }
        if (TxtTimeoutLabel != null)
        {
            TxtTimeoutLabel.Opacity = enabled ? 1.0 : 0.4;
        }
    }

    private void BtnBacklightOff_Click(object sender, RoutedEventArgs e)
    {
        UpdateLevelButtons(KeyboardBacklightLevel.Off);
        _ = AcerWmiManager.SetBacklightLevelAsync(KeyboardBacklightLevel.Off);
    }

    private void BtnBacklightL1_Click(object sender, RoutedEventArgs e)
    {
        UpdateLevelButtons(KeyboardBacklightLevel.Level1);
        _ = AcerWmiManager.SetBacklightLevelAsync(KeyboardBacklightLevel.Level1);
    }

    private void BtnBacklightL2_Click(object sender, RoutedEventArgs e)
    {
        UpdateLevelButtons(KeyboardBacklightLevel.Level2);
        _ = AcerWmiManager.SetBacklightLevelAsync(KeyboardBacklightLevel.Level2);
    }

    private void ChkBacklightTimeout_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi || ChkBacklightTimeout == null) return;
        _ = AcerWmiManager.SetBacklightTimeoutAsync(ChkBacklightTimeout.IsChecked == true);
    }

    public void RefreshHotkeys()
    {
        if (TxtHkCycle != null)
        {
            TxtHkCycle.Text = HotkeyFormatter.Format(SettingsManager.Get("Hotkey_CyclePower", ""));
            TxtHkDash.Text = HotkeyFormatter.Format(SettingsManager.Get("Hotkey_Dashboard", ""));
        }
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true; // Prevent standard typing

        if (sender is not TextBox txt || txt.Tag == null) return;

        // Ignore modifier keys pressed on their own
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.System)
            return;

        string settingKey = (string)txt.Tag;

        // ESC clears the hotkey
        if (e.Key == Key.Escape)
        {
            txt.Text = "None";
            SettingsManager.Save(settingKey, "");
            Keyboard.ClearFocus();
            return;
        }

        // Extract actual key (handle System keys like Alt+Key)
        Key key = (e.Key == Key.System ? e.SystemKey : e.Key);
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        // Calculate modifiers
        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;

        // Require at least one modifier to prevent binding simple letters like "W"
        if (modifiers == 0) return;

        // Save as "Modifiers|VirtualKey"
        string savedValue = HotkeyFormatter.Serialize(modifiers, vk);
        SettingsManager.Save(settingKey, savedValue);

        txt.Text = HotkeyFormatter.Format(savedValue);
        Keyboard.ClearFocus();
    }

    public static string FormatHotkeyLabel(string savedValue) => HotkeyFormatter.Format(savedValue);
}
