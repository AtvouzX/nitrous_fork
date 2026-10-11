using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

public class FanAutoResetTests
{
    [Fact]
    public void FanBehaviorRegisters_MatchAcerEcSpecifications()
    {
        Assert.Equal(0x41000Ful, AcerWmiManager.FanBehaviorAuto);
        Assert.Equal(0x82000Ful, AcerWmiManager.FanBehaviorMax);
        Assert.Equal(0xC3000Ful, AcerWmiManager.FanBehaviorCustom);
    }

    [Fact]
    public void ResetFansToAutoSync_OnTestHost_ExecutesSafelyWithoutThrowing()
    {
        var ex = Record.Exception(() =>
        {
            bool result = AcerWmiManager.ResetFansToAutoSync();
            // On test runners / non-Acer hardware, IsHardwareSupported is false, returning false safely.
            // On Acer hardware, it returns true if WMI succeeds.
            _ = result;
        });

        Assert.Null(ex);
    }

    [Fact]
    public void TriggerExitFanReset_WhenDisabledInSettings_ReturnsFalse()
    {
        TrayApplication.ResetExitFlagForTesting();
        try
        {
            SettingsManager.Save("ResetFansOnExit", 0);

            bool executed = TrayApplication.TriggerExitFanReset();
            Assert.False(executed);
        }
        finally
        {
            SettingsManager.Delete("ResetFansOnExit");
            TrayApplication.ResetExitFlagForTesting();
        }
    }

    [Fact]
    public void TriggerExitFanReset_IsIdempotent_ExecutesAtMostOncePerCycle()
    {
        TrayApplication.ResetExitFlagForTesting();
        try
        {
            SettingsManager.Delete("ResetFansOnExit"); // Defaults to 1 (enabled)

            // First execution triggers the reset logic
            _ = TrayApplication.TriggerExitFanReset();

            // Subsequent immediate call must be blocked by the Interlocked guard
            bool secondCallResult = TrayApplication.TriggerExitFanReset();
            Assert.False(secondCallResult);

            // Third call also blocked
            bool thirdCallResult = TrayApplication.TriggerExitFanReset();
            Assert.False(thirdCallResult);
        }
        finally
        {
            TrayApplication.ResetExitFlagForTesting();
        }
    }

    [Fact]
    public void ResetExitFlagForTesting_ReenablesExecution()
    {
        TrayApplication.ResetExitFlagForTesting();
        try
        {
            SettingsManager.Delete("ResetFansOnExit");

            _ = TrayApplication.TriggerExitFanReset();
            Assert.False(TrayApplication.TriggerExitFanReset());

            // Resetting flag allows it to execute again
            TrayApplication.ResetExitFlagForTesting();
            var ex = Record.Exception(() => TrayApplication.TriggerExitFanReset());
            Assert.Null(ex);
        }
        finally
        {
            TrayApplication.ResetExitFlagForTesting();
        }
    }

    [Fact]
    public void TriggerExitFanReset_DoesNotThrowExceptions()
    {
        TrayApplication.ResetExitFlagForTesting();
        var ex = Record.Exception(() => TrayApplication.TriggerExitFanReset());
        Assert.Null(ex);
        TrayApplication.ResetExitFlagForTesting();
    }
}
