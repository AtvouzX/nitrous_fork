using System;
using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class WhisperModeTests : IDisposable
{
    private readonly List<(uint ControlType, int Value)> _calls = new();

    public WhisperModeTests()
    {
        NvidiaGpuManager.SetWhisperDelegateForTesting((ref NvidiaGpuManager.NvToppsJpacControl ctl) =>
        {
            _calls.Add((ctl.ControlType, ctl.Value));
            return 0;
        });
    }

    public void Dispose()
    {
        NvidiaGpuManager.SetGpuAwakeForTesting(null);
        NvidiaGpuManager.SetWhisperDelegateForTesting(null);
    }

    [Theory]
    [InlineData(PowerProfile.Eco, true)]
    [InlineData(PowerProfile.Quiet, true)]
    [InlineData(PowerProfile.Balanced, false)]
    [InlineData(PowerProfile.Performance, false)]
    [InlineData(PowerProfile.Turbo, false)]
    public void IsWhisperProfile_QuietAndEcoActivateWhisper(PowerProfile profile, bool expected)
    {
        Assert.Equal(expected, NvidiaGpuManager.IsWhisperProfile(profile));
    }

    [Fact]
    public void SetWhisperMode_WhenGpuAsleep_StillApplies()
    {
        // Whisper is a driver preference, so it may wake the dGPU instead of deferring.
        NvidiaGpuManager.SetGpuAwakeForTesting(false);
        using var manager = new NvidiaGpuManager();

        bool applied = manager.SetWhisperMode(true);

        Assert.True(applied);
        Assert.Equal(3, _calls.Count);
        Assert.Equal(
            NvidiaGpuManager.WhisperStateOn,
            SettingsManager.GetInt(NvidiaGpuManager.WhisperStateKey, -1));
    }

    [Fact]
    public void SetWhisperMode_WhenGpuAwake_SendsEnableModeAndFpsTarget()
    {
        NvidiaGpuManager.SetGpuAwakeForTesting(true);
        using var manager = new NvidiaGpuManager();

        bool applied = manager.SetWhisperMode(true);

        Assert.True(applied);
        Assert.Equal(3, _calls.Count);
        Assert.Equal((0u, 1), _calls[0]);  // Enable WhisperMode
        Assert.Equal((1u, 1), _calls[1]);  // Mode = Quiet
        Assert.Equal((2u, 45), _calls[2]); // Target FPS = 45
        Assert.Equal(
            NvidiaGpuManager.WhisperStateOn,
            SettingsManager.GetInt(NvidiaGpuManager.WhisperStateKey, -1));
    }

    [Fact]
    public void SetWhisperMode_WhenDriverRejects_RecordsUnavailable()
    {
        NvidiaGpuManager.SetGpuAwakeForTesting(true);
        NvidiaGpuManager.SetWhisperDelegateForTesting((ref NvidiaGpuManager.NvToppsJpacControl _) => -1);
        using var manager = new NvidiaGpuManager();

        bool applied = manager.SetWhisperMode(true);

        Assert.False(applied);
        Assert.Equal(
            NvidiaGpuManager.WhisperStateUnavailable,
            SettingsManager.GetInt(NvidiaGpuManager.WhisperStateKey, -1));
    }

    [Fact]
    public void SetWhisperMode_WhenFpsCapRejected_DoesNotClaimOn()
    {
        NvidiaGpuManager.SetGpuAwakeForTesting(true);
        NvidiaGpuManager.SetWhisperDelegateForTesting(
            (ref NvidiaGpuManager.NvToppsJpacControl ctl) => ctl.ControlType == 2 ? -1 : 0);
        using var manager = new NvidiaGpuManager();

        bool applied = manager.SetWhisperMode(true);

        Assert.False(applied);
        Assert.Equal(
            NvidiaGpuManager.WhisperStateUnavailable,
            SettingsManager.GetInt(NvidiaGpuManager.WhisperStateKey, -1));
    }

    [Fact]
    public void SetWhisperMode_WhenDisabling_SendsDisableCommand()
    {
        NvidiaGpuManager.SetGpuAwakeForTesting(true);
        using var manager = new NvidiaGpuManager();

        bool applied = manager.SetWhisperMode(false);

        Assert.True(applied);
        Assert.Single(_calls);
        Assert.Equal((0u, 0), _calls[0]); // Disable WhisperMode
    }

    [Fact]
    public void SetWhisperMode_Idempotent_SkipsDuplicateInvocation()
    {
        NvidiaGpuManager.SetGpuAwakeForTesting(true);
        using var manager = new NvidiaGpuManager();

        Assert.True(manager.SetWhisperMode(true));
        Assert.Equal(3, _calls.Count);

        // Second call with same state should skip
        Assert.True(manager.SetWhisperMode(true));
        Assert.Equal(3, _calls.Count);
    }
}
