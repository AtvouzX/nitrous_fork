using System;

namespace Nitrous.Helpers;

/// <summary>
/// Asymmetric fan slew: temperature rise is applied immediately, descent is rate-limited.
/// Prevents audible revving during short burst loads.
/// </summary>
public sealed class FanRampState
{
    private int _applied = -1;
    private DateTime? _belowSince;

    public int Applied => _applied;

    public void Reset()
    {
        _applied = -1;
        _belowSince = null;
    }

    public int Next(
        int target,
        int temperature,
        int ratePerSecond,
        TimeSpan tick,
        TimeSpan hold,
        DateTime now)
    {
        if (_applied < 0 ||
            target >= _applied ||
            temperature >= FanRamp.NeverRampDownAtOrAbove ||
            ratePerSecond <= 0)
        {
            _applied = target;
            _belowSince = null;
            return _applied;
        }

        _belowSince ??= now;
        if (now - _belowSince.Value < hold)
            return _applied;

        int step = Math.Max(1, (int)Math.Round(ratePerSecond * tick.TotalSeconds));
        _applied = Math.Max(target, _applied - step);
        return _applied;
    }
}

public static class FanRamp
{
    public const int NeverRampDownAtOrAbove = 90;

    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);

    public const string Off = "Off";
    public const string Gentle = "Gentle";
    public const string Balanced = "Balanced";
    public const string Fast = "Fast";

    public static readonly string[] Options = { Off, Gentle, Balanced, Fast };

    public static int PerSecondFor(string? setting) => setting switch
    {
        Off => 0,
        Gentle => 5,
        Fast => 20,
        _ => 10 // Balanced default
    };
}
