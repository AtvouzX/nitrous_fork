using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Frozen;
using Nitrous.Interop;

namespace Nitrous.Hooks;

public partial class NitroKeyHook : IDisposable
{
    private const int WM_SYSKEYDOWN = 0x0104;

    private readonly IntPtr _hookID = IntPtr.Zero;
    private readonly User32.LowLevelKeyboardProc _proc;

    public event EventHandler? NitroKeyPressed;

    private static readonly FrozenSet<uint> NitroScanCodes = new[] { 117u, 175u, 236u }.ToFrozenSet();

    public NitroKeyHook()
    {
        _proc = HookCallback;
        _hookID = SetHook(_proc);
        Managers.LogManager.Debug("NitroKeyHook installed WH_KEYBOARD_LL hook.", "NitroKeyHook");
    }

    private static IntPtr SetHook(User32.LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        string? moduleName = curModule?.ModuleName;
        IntPtr hMod = string.IsNullOrEmpty(moduleName) ? IntPtr.Zero : Kernel32.GetModuleHandle(moduleName);
        return User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, proc, hMod, 0);
    }

    private long _lastTriggerTick = 0;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Only listen for key down events (ignore key releases)
        if (nCode >= 0 && (wParam == (IntPtr)User32.WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kbd = Marshal.PtrToStructure<User32.KBDLLHOOKSTRUCT>(lParam);

            if (NitroScanCodes.Contains(kbd.scanCode))
            {
                long now = Environment.TickCount64;
                // 500ms debounce to prevent multi-event flooding from single keypress
                if (now - _lastTriggerTick > 500)
                {
                    _lastTriggerTick = now;
                    Managers.LogManager.Info($"Physical Nitro key detected (scanCode: {kbd.scanCode}). Dispatching activation.", "NitroKeyHook");
                    // Asynchronously dispatch the event so the OS keyboard hook callback returns immediately
                    ThreadPool.QueueUserWorkItem(_ => NitroKeyPressed?.Invoke(this, EventArgs.Empty));
                }
            }
        }

        return User32.CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        User32.UnhookWindowsHookEx(_hookID);
        Managers.LogManager.Debug("NitroKeyHook unhooked.", "NitroKeyHook");
    }
}
