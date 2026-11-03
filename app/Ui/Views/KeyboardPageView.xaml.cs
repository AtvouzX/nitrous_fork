using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nitrous.Helpers;
using Nitrous.Interop;
using Nitrous.Managers;
using Nitrous.Ui.Controls;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace Nitrous.Ui.Views;

public partial class KeyboardPageView : UserControl
{
    private static readonly (string Name, KeyboardEffect Effect)[] KeyboardEffects =
    {
        ("Static", KeyboardEffect.Static),
        ("Breathing", KeyboardEffect.Breathing),
        ("Neon", KeyboardEffect.Neon),
        ("Wave", KeyboardEffect.Wave),
        ("Shifting", KeyboardEffect.Shifting),
        ("Zoom", KeyboardEffect.Zoom),
        ("Meteor", KeyboardEffect.Meteor),
        ("Twinkling", KeyboardEffect.Twinkling)
    };

    private readonly KeyboardColor[] _zoneColors =
    {
        KeyboardColor.White,
        KeyboardColor.White,
        KeyboardColor.White,
        KeyboardColor.White
    };

    private bool _isUpdatingUi;
    private bool _stickyKeysLoading;
    private bool _keyboardLoading;
    private bool _keyboardAvailable;
    private bool _keyboardZoned;
    private KeyboardEffect _keyboardEffect = KeyboardEffect.Static;

    public KeyboardPageView()
    {
        InitializeComponent();
        InitializeStickyKeys();
        InitializeKeyboardLighting();
        RefreshHotkeys();
        RefreshBacklightUi();
    }

    private void InitializeStickyKeys()
    {
        _stickyKeysLoading = true;
        try
        {
            var state = new User32.STICKYKEYS { cbSize = (uint)Marshal.SizeOf<User32.STICKYKEYS>() };
            if (User32.SystemParametersInfoW(User32.SPI_GETSTICKYKEYS, 0, ref state, 0))
            {
                TogStickyKeys.IsChecked = (state.dwFlags & User32.SKF_STICKYKEYSON) != 0;
            }
            else
            {
                TogStickyKeys.IsEnabled = false;
                TogStickyKeys.ToolTip = "Windows did not report the sticky keys setting.";
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Sticky Keys: could not read setting: {ex.Message}", "KeyboardPage");
        }
        _stickyKeysLoading = false;
    }

    private void StickyKeys_Toggled(object sender, RoutedEventArgs e)
    {
        if (_stickyKeysLoading) return;
        try
        {
            var state = new User32.STICKYKEYS { cbSize = (uint)Marshal.SizeOf<User32.STICKYKEYS>() };
            if (!User32.SystemParametersInfoW(User32.SPI_GETSTICKYKEYS, 0, ref state, 0))
            {
                LogManager.Warn("Sticky Keys: could not read setting before writing", "KeyboardPage");
                return;
            }

            bool wanted = TogStickyKeys.IsChecked == true;
            state.dwFlags = KeyboardZoneCodec.UpdateStickyKeysFlags(state.dwFlags, wanted);

            if (!User32.SystemParametersInfoW(
                User32.SPI_SETSTICKYKEYS,
                0,
                ref state,
                User32.SPIF_UPDATEINIFILE | User32.SPIF_SENDCHANGE))
            {
                LogManager.Warn("Sticky Keys: Windows refused the change", "KeyboardPage");
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Sticky Keys toggle exception: {ex.Message}", "KeyboardPage");
        }
    }

    public void InitializeKeyboardLighting()
    {
        _keyboardLoading = true;
        _keyboardAvailable = AcerKeyboard.IsAvailable();
        _keyboardZoned = AcerCapabilitiesProvider.Current.Smbios.KeyboardColor != 1;

        for (int i = 0; i < AcerKeyboard.ZoneCount; i++)
        {
            _zoneColors[i] = KeyboardColor.FromHex(SettingsManager.Get($"KbZone{i + 1}", _zoneColors[i].ToHex()));
        }

        KeyboardEffectBox.Items.Clear();
        foreach (var (name, _) in KeyboardEffects)
        {
            KeyboardEffectBox.Items.Add(name);
        }

        string savedEffect = SettingsManager.Get("KbEffect", "Static");
        int selected = Array.FindIndex(KeyboardEffects, e => e.Name == savedEffect);
        KeyboardEffectBox.SelectedIndex = selected >= 0 ? selected : 0;
        _keyboardEffect = KeyboardEffects[KeyboardEffectBox.SelectedIndex].Effect;

        GrdZoneSwatches.Visibility = _keyboardZoned ? Visibility.Visible : Visibility.Collapsed;
        TxtLightingStatus.Text = !_keyboardAvailable
            ? "Keyboard not supported"
            : !_keyboardZoned
                ? "Single-color keyboard"
                : string.Empty;

        KeyboardEffectBox.IsEnabled = _keyboardAvailable;
        UpdateZoneButtons();
        _keyboardLoading = false;
    }

    private void UpdateZoneButtons()
    {
        var buttons = new[] { Zone1Btn, Zone2Btn, Zone3Btn, Zone4Btn };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Background = new SolidColorBrush(Color.FromRgb(_zoneColors[i].R, _zoneColors[i].G, _zoneColors[i].B));
        }

        ZoneAllBtn.Background = BuildZonePreview();
    }

    private Brush BuildZonePreview()
    {
        var first = _zoneColors[0];
        bool uniform = true;
        for (int i = 1; i < _zoneColors.Length; i++)
        {
            if (_zoneColors[i] != first)
            {
                uniform = false;
                break;
            }
        }

        if (uniform)
        {
            return new SolidColorBrush(Color.FromRgb(first.R, first.G, first.B));
        }

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        for (int i = 0; i < _zoneColors.Length; i++)
        {
            var col = Color.FromRgb(_zoneColors[i].R, _zoneColors[i].G, _zoneColors[i].B);
            gradient.GradientStops.Add(new GradientStop(col, (double)i / (_zoneColors.Length - 1)));
        }
        return gradient;
    }

    private void Zone_Click(object sender, RoutedEventArgs e)
    {
        if (!_keyboardAvailable || !_keyboardZoned ||
            sender is not Button btn || btn.Tag is not string tag ||
            !int.TryParse(tag, out int zone))
        {
            return;
        }

        bool allZones = zone == 0;
        int index = zone - 1;
        if (!allZones && (index < 0 || index >= _zoneColors.Length)) return;

        var current = _zoneColors[allZones ? 0 : index];

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var chosen = new KeyboardColor(dialog.Color.R, dialog.Color.G, dialog.Color.B);

        if (allZones)
        {
            for (int i = 0; i < _zoneColors.Length; i++)
            {
                _zoneColors[i] = chosen;
                SettingsManager.Save($"KbZone{i + 1}", chosen.ToHex());
            }
        }
        else
        {
            _zoneColors[index] = chosen;
            SettingsManager.Save($"KbZone{zone}", chosen.ToHex());
        }

        UpdateZoneButtons();

        if (_keyboardEffect != KeyboardEffect.Static)
        {
            KeyboardEffectBox.SelectedIndex = 0;
            return;
        }

        ApplyKeyboardLighting();
    }

    private void KeyboardEffect_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_keyboardLoading || KeyboardEffectBox.SelectedIndex < 0) return;

        _keyboardEffect = KeyboardEffects[KeyboardEffectBox.SelectedIndex].Effect;
        SettingsManager.Save("KbEffect", KeyboardEffects[KeyboardEffectBox.SelectedIndex].Name);
        ApplyKeyboardLighting();
    }

    private void ApplyKeyboardLighting()
    {
        if (!_keyboardAvailable) return;

        try
        {
            bool applied = _keyboardEffect == KeyboardEffect.Static
                ? AcerKeyboard.SetZoneColors(_zoneColors, 100)
                : AcerKeyboard.SetEffect(_keyboardEffect, 100, 4, leftwards: false, _zoneColors[0]);

            TxtLightingStatus.Text = applied ? string.Empty : "Refused";
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Keyboard lighting apply failed: {ex.Message}", "KeyboardPage");
            TxtLightingStatus.Text = "Failed";
        }
    }

    public void RefreshBacklightUi()
    {
        _isUpdatingUi = true;
        try
        {
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

    private void ChkBacklightTimeout_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi || ChkBacklightTimeout == null) return;
        // Save only: Tray is sole physical applier via SyncBacklightSettingsIfChanged.
        // Uncheck-while-Off cancel/restore is handled by Tray watchdog tick.
        SettingsManager.Save("KeyboardBacklightTimeout", ChkBacklightTimeout.IsChecked == true);
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
        e.Handled = true;

        if (sender is not TextBox txt || txt.Tag == null) return;

        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.System)
            return;

        string settingKey = (string)txt.Tag;

        if (e.Key == Key.Escape)
        {
            txt.Text = "None";
            SettingsManager.Save(settingKey, "");
            Keyboard.ClearFocus();
            return;
        }

        Key key = (e.Key == Key.System ? e.SystemKey : e.Key);
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;

        if (modifiers == 0) return;

        string savedValue = HotkeyFormatter.Serialize(modifiers, vk);
        SettingsManager.Save(settingKey, savedValue);

        txt.Text = HotkeyFormatter.Format(savedValue);
        Keyboard.ClearFocus();
    }

    public static string FormatHotkeyLabel(string savedValue) => HotkeyFormatter.Format(savedValue);
}
