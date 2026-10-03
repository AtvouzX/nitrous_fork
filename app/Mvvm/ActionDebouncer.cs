using System;
using System.Threading;

namespace Nitrous.Mvvm;

public class ActionDebouncer : IDisposable
{
    private readonly TimerCallback _callback;
    private System.Threading.Timer? _timer;
    private Action? _pendingAction;
    private readonly object _lock = new();

    public ActionDebouncer()
    {
        _callback = _ =>
        {
            Action? action;
            lock (_lock)
            {
                action = _pendingAction;
                _pendingAction = null;
            }
            action?.Invoke();
        };
    }

    public void Debounce(int milliseconds, Action action)
    {
        lock (_lock)
        {
            _pendingAction = action;
            if (_timer == null)
            {
                _timer = new System.Threading.Timer(_callback, null, milliseconds, Timeout.Infinite);
            }
            else
            {
                _timer.Change(milliseconds, Timeout.Infinite);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _pendingAction = null;
        }
    }
}
