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
        return EvaluateTick(timeoutEnabled, userLevel, idleDuration, offPending: false, timeoutThreshold);
    }

    /// <summary>
    /// Pending-aware tick for the armed-Off flow: Nitrous writes the Off state while the light
    /// is still on, and the EC only executes it after 30s of real idle. When <paramref name="offPending"/>
    /// is true and the user level is Off, an idle threshold crossing means the EC has taken the
    /// hardware dark (confirm with DimToOff); any earlier tick stays None so the UI keeps showing
    /// "Off in 30s" instead of claiming Off while the light is visibly on.
    /// </summary>
    public WatchdogAction EvaluateTick(
        bool timeoutEnabled,
        KeyboardBacklightLevel userLevel,
        TimeSpan idleDuration,
        bool offPending,
        TimeSpan? timeoutThreshold = null)
    {
        var threshold = timeoutThreshold ?? DefaultTimeout;

        if (userLevel == KeyboardBacklightLevel.Off)
        {
            if (!offPending || !timeoutEnabled)
            {
                IsDimmed = false;
                return WatchdogAction.None;
            }

            if (idleDuration >= threshold)
            {
                if (!IsDimmed)
                {
                    IsDimmed = true;
                    return WatchdogAction.DimToOff;
                }
                return WatchdogAction.None;
            }

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

