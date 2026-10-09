using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Nitrous.Managers;

public class GlobalHotkeyManager : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    public event Action<int>? HotkeyPressed;

    public GlobalHotkeyManager()
    {
        CreateHandle(new CreateParams()); // Creates a hidden message pump
    }

    public void Register(int id, uint modifiers, uint key)
    {
        UnregisterHotKey(this.Handle, id); // Clear existing
        bool success = RegisterHotKey(this.Handle, id, modifiers, key);
        string formatted = FormatHotkey(modifiers, key);
        if (!success)
        {
            LogManager.Warn($"RegisterHotKey failed for ID {id} ('{formatted}'). Another program may be using this shortcut.", "GlobalHotkey");
        }
        else
        {
            LogManager.Debug($"Registered global hotkey ID {id} ('{formatted}')", "GlobalHotkey");
        }
    }

    private static string FormatHotkey(uint modifiers, uint key)
    {
        var sb = new System.Text.StringBuilder();
        if ((modifiers & 0x0002) != 0) sb.Append("Ctrl + ");
        if ((modifiers & 0x0004) != 0) sb.Append("Shift + ");
        if ((modifiers & 0x0001) != 0) sb.Append("Alt + ");
        if ((modifiers & 0x0008) != 0) sb.Append("Win + ");
        sb.Append(((Keys)key).ToString());
        return sb.ToString();
    }

    public void UnregisterAll(params int[] ids)
    {
        foreach (var id in ids)
        {
            UnregisterHotKey(this.Handle, id);
        }
        LogManager.Debug($"Unregistered {ids.Length} global hotkeys", "GlobalHotkey");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
