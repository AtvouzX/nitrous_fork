using System;
using System.Windows.Input;

namespace Nitrous.Helpers;

/// <summary>
/// Pure helper for parsing, serializing, and formatting keyboard hotkeys and modifier flags.
/// </summary>
public static class HotkeyFormatter
{
    public const uint ModAlt = 0x0001;
    public const uint ModCtrl = 0x0002;
    public const uint ModShift = 0x0004;

    /// <summary>
    /// Formats a saved serialized hotkey string ("{modifiers}|{virtualKey}") into a human-readable display string
    /// such as "Ctrl + Shift + Q" or "Alt + D". Returns "None" if empty or malformed.
    /// </summary>
    public static string Format(string? savedValue)
    {
        if (string.IsNullOrWhiteSpace(savedValue)) return "None";

        if (TryParse(savedValue, out uint mods, out uint vk))
        {
            return Format(mods, vk);
        }

        return "None";
    }

    /// <summary>
    /// Formats modifier flags and a virtual key code into a human-readable display string.
    /// </summary>
    public static string Format(uint mods, uint vk)
    {
        string label = "";
        if ((mods & ModCtrl) != 0) label += "Ctrl + ";
        if ((mods & ModShift) != 0) label += "Shift + ";
        if ((mods & ModAlt) != 0) label += "Alt + ";

        string keyName = KeyInterop.KeyFromVirtualKey((int)vk).ToString();
        return label + keyName;
    }

    /// <summary>
    /// Parses a serialized hotkey string ("{modifiers}|{virtualKey}") into modifier flags and virtual key code.
    /// </summary>
    public static bool TryParse(string? savedValue, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(savedValue)) return false;

        var parts = savedValue.Split('|');
        if (parts.Length != 2) return false;

        if (uint.TryParse(parts[0], out uint m) && uint.TryParse(parts[1], out uint vk))
        {
            modifiers = m;
            virtualKey = vk;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Serializes modifier flags and a virtual key code into storage format ("{modifiers}|{virtualKey}").
    /// </summary>
    public static string Serialize(uint modifiers, uint virtualKey) => $"{modifiers}|{virtualKey}";
}
