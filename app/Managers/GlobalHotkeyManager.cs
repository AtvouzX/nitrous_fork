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
        RegisterHotKey(this.Handle, id, modifiers, key);
    }

    public void UnregisterAll(params int[] ids)
    {
        foreach (var id in ids) UnregisterHotKey(this.Handle, id);
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
