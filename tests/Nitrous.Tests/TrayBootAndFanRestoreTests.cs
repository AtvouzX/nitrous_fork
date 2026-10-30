using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Nitrous.Enums;
using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

// Regression tests for review verdict (Axis 2 behavioral invariance):
// 1. Boot OC must honor the AutoApplyGpuClocks user opt-out (old-master
//    ApplyOnBootAsync -> AutoApplyClocksAsync aborted when disabled).
// 2. AC/DC restore must restore fans for non-Medium modes (old master
//    called SetFansAsync(activeFan) alongside the power switch).
public class TrayBootAndFanRestoreTests : IDisposable
{
    private readonly int _origAutoSwitch;
    private readonly int _origLastPower;
    private readonly int _origAcPower;
    private readonly int _origDcPower;
    private readonly string _origAcFan;
    private readonly string _origDcFan;
    private readonly string _origLastFan;
    private readonly int _origAutoApply;

    public TrayBootAndFanRestoreTests()
    {
        _origAutoSwitch = SettingsManager.Get("AutoSwitch", 0);
        _origLastPower = SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        _origAcPower = SettingsManager.Get("LastAcPowerMode", (int)PowerProfile.Performance);
        _origDcPower = SettingsManager.Get("LastDcPowerMode", (int)PowerProfile.Quiet);
        _origAcFan = SettingsManager.Get("LastAcFanMode", "Auto");
        _origDcFan = SettingsManager.Get("LastDcFanMode", "Auto");
        _origLastFan = SettingsManager.Get("LastFanMode", "Auto");
        _origAutoApply = SettingsManager.GetInt("AutoApplyGpuClocks", 1);
    }

    public void Dispose()
    {
        AcerWmiManager.SetWmiInvokeForTesting(null);
        AcerWmiManager.SetTurboSupportedForTesting(null);
        SettingsManager.Save("AutoSwitch", _origAutoSwitch);
        SettingsManager.Save("LastPowerMode", _origLastPower);
        SettingsManager.Save("LastAcPowerMode", _origAcPower);
        SettingsManager.Save("LastDcPowerMode", _origDcPower);
        SettingsManager.Save("LastAcFanMode", _origAcFan);
        SettingsManager.Save("LastDcFanMode", _origDcFan);
        SettingsManager.Save("LastFanMode", _origLastFan);
        SettingsManager.Save("AutoApplyGpuClocks", _origAutoApply);
    }

    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }

    [Fact]
    public void BootOc_WhenAutoApplyDisabled_SkipsOc()
    {
        SettingsManager.Save("AutoApplyGpuClocks", 0);
        Assert.False(TrayApplication.ShouldApplyBootOc(true));

        SettingsManager.Save("AutoApplyGpuClocks", 1);
        Assert.True(TrayApplication.ShouldApplyBootOc(true));

        Assert.False(TrayApplication.ShouldApplyBootOc(false));
    }

    [Theory]
    [InlineData(FanProfile.Max)]
    [InlineData(FanProfile.Quiet)]
    public void AcDcRestore_WithNonMediumFan_RestoresFanViaSetFans(FanProfile fan)
    {
        RunInSta(() =>
        {
            var calls = new List<(string Class, string Method, ulong Input)>();
            AcerWmiManager.SetTurboSupportedForTesting(true);
            AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
            {
                lock (calls)
                {
                    calls.Add((cls, method, input));
                }
                return true;
            });

            SettingsManager.Save("AutoSwitch", 1);
            SettingsManager.Save("LastPowerMode", (int)PowerProfile.Quiet);
            SettingsManager.Save("LastAcPowerMode", (int)PowerProfile.Performance);
            SettingsManager.Save("LastDcPowerMode", (int)PowerProfile.Quiet);
            SettingsManager.Save("LastAcFanMode", fan.ToString());

            using var tray = new TrayApplication();
            tray.WasOnAcPower = false;
            tray.LastOsdProfile = null;

            tray.ApplyPowerSettings(isStartup: false, isOnlineOverride: true);

            ulong expectedBehavior = fan == FanProfile.Max
                ? AcerWmiManager.FanBehaviorMax
                : AcerWmiManager.FanBehaviorCustom;
            bool fanTouched = WaitFor(() =>
            {
                lock (calls)
                {
                    return calls.Exists(c => c.Method == "SetGamingFanBehavior" && c.Input == expectedBehavior);
                }
            });
            Assert.True(fanTouched, $"AC restore with {fan} fan must touch fan WMI (SetFansAsync).");
        });
    }
}
