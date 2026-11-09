using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Nitrous.Enums;
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

    [Fact]
    public async Task SetFansAsync_WithProfileAndCustomSpeeds_RoutesThroughCanonicalFanPath()
    {
        List<(string Class, string Method, ulong Input)> wmiCalls = new();
        AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
        {
            lock (wmiCalls)
            {
                wmiCalls.Add((cls, method, input));
            }
            return true;
        });
        try
        {
            // The fan gate reads the ambient power mode; pin a permissive one so this
            // test does not depend on the host machine's real LastPowerMode value.
            int origPowerMode = SettingsManager.GetInt("LastPowerMode", (int)PowerProfile.Performance);
            SettingsManager.Save("LastPowerMode", (int)PowerProfile.Performance);
            try
            {
                await AcerWmiManager.SetFansAsync(FanProfile.Auto);
                Assert.Contains(wmiCalls, c => c.Method == "SetGamingFanBehavior" && c.Input == AcerWmiManager.FanBehaviorAuto);
                wmiCalls.Clear();
                await AcerWmiManager.SetFansAsync(FanProfile.Max);
                Assert.Contains(wmiCalls, c => c.Method == "SetGamingFanBehavior" && c.Input == AcerWmiManager.FanBehaviorMax);
                wmiCalls.Clear();
                await AcerWmiManager.SetFansAsync(FanProfile.Medium, 60, 70);
                Assert.Contains(wmiCalls, c => c.Method == "SetGamingFanBehavior" && c.Input == AcerWmiManager.FanBehaviorCustom);
                Assert.Contains(wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (1ul | (60ul << 8)));
                Assert.Contains(wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (2ul | (70ul << 8)));
                Assert.Contains(wmiCalls, c => c.Method == "SetGamingFanSpeed" && c.Input == (4ul | (70ul << 8)));
            }
            finally
            {
                SettingsManager.Save("LastPowerMode", origPowerMode);
            }
        }
        finally
        {
            AcerWmiManager.SetWmiInvokeForTesting(null);
        }
    }

    [Fact]
    public void SetCustomFansAsync_DoesNotExistAsPublicContract()
    {
        MethodInfo? dupe = typeof(AcerWmiManager).GetMethod("SetCustomFansAsync");
        Assert.Null(dupe);
    }

    [Fact]
    public void InvokeWmiSync_IsPrivate()
    {
        MethodInfo? sync = typeof(AcerWmiManager).GetMethod("InvokeWmiSync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(sync);
        Assert.True(sync!.IsPrivate);
        MethodInfo? leaked = typeof(AcerWmiManager).GetMethod("InvokeWmiSync");
        Assert.Null(leaked);
    }

    [Fact]
    public void SetChargeLimitAsync_MovedToBatteryManagerOnly()
    {
        MethodInfo? legacy = typeof(AcerWmiManager).GetMethod("SetChargeLimitAsync");
        Assert.Null(legacy);
        MethodInfo? canonical = typeof(BatteryManager).GetMethod("SetChargeLimitAsync");
        Assert.NotNull(canonical);
    }
}
