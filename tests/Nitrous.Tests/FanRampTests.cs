using System;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class FanRampTests
{
    [Fact]
    public void Next_ImmediateRise_JumpsDirectlyToTarget()
    {
        var ramp = new FanRampState();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tick = TimeSpan.FromSeconds(2);
        var hold = TimeSpan.FromSeconds(3);

        // Initial setup
        int initial = ramp.Next(40, 50, 10, tick, hold, now);
        Assert.Equal(40, initial);

        // Jump upward
        now += tick;
        int rise = ramp.Next(80, 70, 10, tick, hold, now);
        Assert.Equal(80, rise);
        Assert.Equal(80, ramp.Applied);
    }

    [Fact]
    public void Next_Descent_HoldsDuringDwell_ThenStepsDownBounded()
    {
        var ramp = new FanRampState();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tick = TimeSpan.FromSeconds(2);
        var hold = TimeSpan.FromSeconds(3);
        int rate = 10; // 10%/sec => 20% per 2s tick

        // Prime at 80%
        ramp.Next(80, 60, rate, tick, hold, start);

        // Tick 1 (t = 2s): wanted = 30%. Elapsed since below = 0s (< 3s hold) => stays 80%
        var t1 = start + TimeSpan.FromSeconds(2);
        int step1 = ramp.Next(30, 50, rate, tick, hold, t1);
        Assert.Equal(80, step1);

        // Tick 2 (t = 4s): elapsed since below = 2s (< 3s hold) => stays 80%
        var t2 = start + TimeSpan.FromSeconds(4);
        int step2 = ramp.Next(30, 50, rate, tick, hold, t2);
        Assert.Equal(80, step2);

        // Tick 3 (t = 6s): elapsed since below = 4s (>= 3s hold) => steps down by 20% (80 - 20 = 60)
        var t3 = start + TimeSpan.FromSeconds(6);
        int step3 = ramp.Next(30, 50, rate, tick, hold, t3);
        Assert.Equal(60, step3);

        // Tick 4 (t = 8s): steps down another 20% (60 - 20 = 40)
        var t4 = start + TimeSpan.FromSeconds(8);
        int step4 = ramp.Next(30, 50, rate, tick, hold, t4);
        Assert.Equal(40, step4);

        // Tick 5 (t = 10s): target is 30, remaining drop is 10 (< 20 step) => clamps to target 30
        var t5 = start + TimeSpan.FromSeconds(10);
        int step5 = ramp.Next(30, 50, rate, tick, hold, t5);
        Assert.Equal(30, step5);
    }

    [Fact]
    public void Next_HotChip_BypassesRampAndHold()
    {
        var ramp = new FanRampState();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tick = TimeSpan.FromSeconds(2);
        var hold = TimeSpan.FromSeconds(3);

        ramp.Next(80, 70, 10, tick, hold, start);

        // Descent wanted, but temperature is 92C (>= 90C override)
        int result = ramp.Next(50, 92, 10, tick, hold, start + tick);
        Assert.Equal(50, result);
    }

    [Fact]
    public void Reset_ClearsAppliedAndHoldTracking()
    {
        var ramp = new FanRampState();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tick = TimeSpan.FromSeconds(2);
        var hold = TimeSpan.FromSeconds(3);

        ramp.Next(70, 60, 10, tick, hold, start);
        Assert.Equal(70, ramp.Applied);

        ramp.Reset();
        Assert.Equal(-1, ramp.Applied);

        // Next call behaves as initial
        int reapply = ramp.Next(50, 60, 10, tick, hold, start + tick);
        Assert.Equal(50, reapply);
    }

    [Theory]
    [InlineData("Off", 0)]
    [InlineData("Gentle", 5)]
    [InlineData("Balanced", 10)]
    [InlineData("Fast", 20)]
    [InlineData("Unknown", 10)]
    [InlineData(null, 10)]
    public void FanRamp_PerSecondFor_ResolvesCorrectRate(string? setting, int expectedRate)
    {
        Assert.Equal(expectedRate, FanRamp.PerSecondFor(setting));
    }
}
