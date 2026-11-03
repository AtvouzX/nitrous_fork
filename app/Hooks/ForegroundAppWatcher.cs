using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Nitrous.Interop;
using Nitrous.Managers;

namespace Nitrous.Hooks;

/// <summary>
/// Event-driven foreground app watcher for Scenario App Sync.
/// Fires only when the foreground PID changes; exe file name is resolved
/// in-process on change (bounded PID cache). No polling, no enumeration,
/// no child process. Nitrous' own process is reported as null (ignored).
/// </summary>
public sealed class ForegroundAppWatcher : IDisposable
{
    private const int MaxCacheEntries = 64;

    private readonly Dictionary<uint, string> _exeCache = new();
    private readonly object _cacheLock = new();
    private User32.WinEventProc? _winEventProc; // rooted for hook lifetime
    private IntPtr _hook;
    private uint _lastPid;
    private bool _disposed;

    /// <summary>Raised with the foreground exe file name, or null when ignored/unresolvable.</summary>
    public event EventHandler<string?>? ForegroundExeChanged;

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;

        _winEventProc = OnWinEvent;
        _hook = User32.SetWinEventHook(
            User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, User32.WINEVENT_OUTOFCONTEXT);

        if (_hook == IntPtr.Zero)
        {
            LogManager.Warn("ForegroundAppWatcher: SetWinEventHook failed; App Sync disabled", "AppSync");
            return false;
        }
        return true;
    }

    /// <summary>Re-reports the current foreground window (e.g. after session unlock).</summary>
    public void RaiseCurrentForeground()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd != IntPtr.Zero) HandleForeground(hwnd, force: true);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_disposed || hwnd == IntPtr.Zero) return;
        HandleForeground(hwnd, force: false);
    }

    private void HandleForeground(IntPtr hwnd, bool force)
    {
        try
        {
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;
            if (!force && pid == _lastPid) return;
            _lastPid = pid;

            if (pid == (uint)Environment.ProcessId)
            {
                ForegroundExeChanged?.Invoke(this, null); // own process: ignored by consumer
                return;
            }

            ForegroundExeChanged?.Invoke(this, ResolveExeFileName(pid));
        }
        catch (Exception ex)
        {
            LogManager.Debug($"ForegroundAppWatcher callback failed: {ex.Message}", "AppSync");
        }
    }

    private string? ResolveExeFileName(uint pid)
    {
        lock (_cacheLock)
        {
            if (_exeCache.TryGetValue(pid, out string? cached)) return cached;
        }

        string? fileName = null;
        IntPtr handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                if (Kernel32.QueryFullProcessImageName(handle, 0, buffer, ref size))
                {
                    fileName = Path.GetFileName(buffer.ToString());
                }
            }
            finally
            {
                Kernel32.CloseHandle(handle);
            }
        }

        if (fileName != null)
        {
            lock (_cacheLock)
            {
                if (_exeCache.Count >= MaxCacheEntries) _exeCache.Clear();
                _exeCache[pid] = fileName;
            }
        }
        return fileName;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            User32.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _winEventProc = null;
    }
}
