using System;
using System.Management;
using Nitrous.Enums;
using Nitrous.Managers;

namespace Nitrous.Helpers;

/// <summary>
/// Listens for EC backlight-hotkey broadcasts (Fn+F11) via the Acer APGe WMI event channel.
/// Proven live on ANV15-41 (BIOS reports ANV14-41): each Fn+F11 press raises
/// <c>APGeEvent</c> on <c>root\WMI</c> with <c>EventDetail = [01 84 08 00 ...]</c>
/// (byte[1] = 0x84 = BK_Hotkey_Number). The event carries no brightness level;
/// Nitrous tracks the EC's off -&gt; dim -&gt; bright cycle in settings instead.
/// Event-driven only: no polling, no timer, zero idle cost. Safe no-op when the
/// provider is absent (logs a warning and stays stopped).
/// </summary>
public sealed class BacklightHotkeyWatcher : IDisposable
{
    public const byte BacklightHotkeyNumber = 0x84;
    private const string ScopePath = @"\\.\root\WMI";
    private const string QueryText = "SELECT * FROM APGeEvent";

    private ManagementEventWatcher? _watcher;
    private bool _disposed;

    public event EventHandler? BacklightHotkeyPressed;

    public bool IsRunning => _watcher != null;

    /// <summary>
    /// Pure predicate used by the live handler and unit tests.
    /// </summary>
    public static bool IsBacklightHotkeyEvent(byte[]? eventDetail)
    {
        return eventDetail != null && eventDetail.Length > 1 && eventDetail[1] == BacklightHotkeyNumber;
    }

    /// <summary>
    /// Advances Nitrous' tracked level through the EC's fixed cycle: off -&gt; dim -&gt; bright -&gt; off.
    /// </summary>
    public static KeyboardBacklightLevel AdvanceCycleLevel(KeyboardBacklightLevel current) => current switch
    {
        KeyboardBacklightLevel.Off => KeyboardBacklightLevel.Level1,
        KeyboardBacklightLevel.Level1 => KeyboardBacklightLevel.Level2,
        _ => KeyboardBacklightLevel.Off,
    };

    public void Start()
    {
        if (_watcher != null || _disposed) return;

        try
        {
            var scope = new ManagementScope(ScopePath);
            scope.Connect();
            var watcher = new ManagementEventWatcher(scope, new WqlEventQuery(QueryText));
            watcher.EventArrived += OnEventArrived;
            watcher.Start();
            _watcher = watcher;
            LogManager.Info("APGeEvent backlight-hotkey watcher started.", "Backlight");
        }
        catch (Exception ex)
        {
            LogManager.Warn($"APGeEvent watcher unavailable, Fn+F11 tracking disabled: {ex.Message}", "Backlight");
        }
    }

    public void Stop()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher == null) return;

        try
        {
            watcher.EventArrived -= OnEventArrived;
            watcher.Stop();
            watcher.Dispose();
        }
        catch (Exception ex)
        {
            LogManager.Debug($"APGeEvent watcher stop failed: {ex.Message}", "Backlight");
        }
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            byte[]? detail = e.NewEvent["EventDetail"] as byte[];
            if (!IsBacklightHotkeyEvent(detail)) return;
            LogManager.Info("Hardware Fn+F11 backlight hotkey detected via APGeEvent.", "Backlight");
            BacklightHotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogManager.Debug($"APGeEvent handling failed: {ex.Message}", "Backlight");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
