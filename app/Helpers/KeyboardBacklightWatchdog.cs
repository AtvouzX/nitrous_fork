using System;
using Nitrous.Enums;

namespace Nitrous.Helpers;

public enum WatchdogAction
{
    None = 0,
    DimToOff = 1,
    RestoreLevel = 2,
}

public class KeyboardBacklightWatchdog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public bool IsDimmed { get; private set; }

    public WatchdogAction EvaluateTick(
        bool timeoutEnabled,
        KeyboardBacklightLevel userLevel,
        TimeSpan idleDuration,
        TimeSpan? timeoutThreshold = null)
    {
        var threshold = timeoutThreshold ?? DefaultTimeout;

        // If user explicitly chose Off, keyboard is already off; nothing to dim or restore
        if (userLevel == KeyboardBacklightLevel.Off)
        {
            IsDimmed = false;
            return WatchdogAction.None;
        }

        // If timeout is disabled, restore if currently dimmed
        if (!timeoutEnabled)
        {
            if (IsDimmed)
            {
                IsDimmed = false;
                return WatchdogAction.RestoreLevel;
            }
            return WatchdogAction.None;
        }

        // Timeout enabled: check idle threshold
        if (idleDuration >= threshold)
        {
            if (!IsDimmed)
            {
                IsDimmed = true;
                return WatchdogAction.DimToOff;
            }
            return WatchdogAction.None;
        }
        else
        {
            if (IsDimmed)
            {
                IsDimmed = false;
                return WatchdogAction.RestoreLevel;
            }
            return WatchdogAction.None;
        }
    }

    public void Reset()
    {
        IsDimmed = false;
    }
}

