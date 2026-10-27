using System;
using System.Windows.Controls;
using System.Windows.Input;
using Nitrous.Managers;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace Nitrous.Ui.Views;

public partial class KeyboardPageView : UserControl
{
    public KeyboardPageView()
    {
        InitializeComponent();
        RefreshHotkeys();
    }

    public void RefreshHotkeys()
    {
        if (TxtHkCycle != null)
        {
            TxtHkCycle.Text = FormatHotkeyLabel(SettingsManager.Get("Hotkey_CyclePower", ""));
            TxtHkDash.Text = FormatHotkeyLabel(SettingsManager.Get("Hotkey_Dashboard", ""));
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
        string savedValue = $"{modifiers}|{vk}";
        SettingsManager.Save(settingKey, savedValue);

        txt.Text = FormatHotkeyLabel(savedValue);
        Keyboard.ClearFocus();
    }

    public static string FormatHotkeyLabel(string savedValue)
    {
        if (string.IsNullOrEmpty(savedValue)) return "None";
        try
        {
            var parts = savedValue.Split('|');
            uint mods = uint.Parse(parts[0]);
            uint vk = uint.Parse(parts[1]);

            string label = "";
            if ((mods & 0x0002) != 0) label += "Ctrl + ";
            if ((mods & 0x0004) != 0) label += "Shift + ";
            if ((mods & 0x0001) != 0) label += "Alt + ";

            label += KeyInterop.KeyFromVirtualKey((int)vk).ToString();
            return label;
        }
        catch
        {
            return "None";
        }
    }
}
