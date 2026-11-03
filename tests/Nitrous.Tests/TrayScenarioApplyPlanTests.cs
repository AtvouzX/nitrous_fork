using Nitrous.Enums;
using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

public class TrayScenarioApplyPlanTests
{
    private static ScenarioProfile Profile() => new()
    {
        Name = "Gaming",
        AcMode = PowerProfile.Turbo,
        DcMode = PowerProfile.Quiet,
        FanMode = FanProfile.Auto,
        CustomFanSpeedCpu = 61,
        CustomFanSpeedGpu = 72,
        FanCurvePreset = "Auto",
    };

    [Fact]
    public void Plan_OnAc_UsesAcMode_WithTurboGate()
    {
        var p = Profile();
        var plan = TrayApplication.BuildScenarioApplyPlan(p, isAcOnline: true, turboSupported: false);
        Assert.Equal(PowerProfile.Performance, plan.EffectiveMode);
        Assert.Equal(ScenarioFanAction.PresetMode, plan.FanAction);
    }

    [Fact]
    public void Plan_OnDc_UsesDcMode()
    {
        var plan = TrayApplication.BuildScenarioApplyPlan(Profile(), isAcOnline: false, turboSupported: true);
        Assert.Equal(PowerProfile.Quiet, plan.EffectiveMode);
    }

    [Fact]
    public void Plan_MediumFanWithoutCurvePreset_UsesFixedSpeeds()
    {
        var p = Profile();
        p.FanMode = FanProfile.Medium;
        var plan = TrayApplication.BuildScenarioApplyPlan(p, isAcOnline: true, turboSupported: true);
        Assert.Equal(ScenarioFanAction.FixedSpeeds, plan.FanAction);
        Assert.Equal(61, plan.CustomFanSpeedCpu);
        Assert.Equal(72, plan.CustomFanSpeedGpu);
    }

    [Fact]
    public void Plan_NamedCurvePreset_UsesCurveEngine()
    {
        var p = Profile();
        p.FanMode = FanProfile.Medium;
        p.FanCurvePreset = "Custom 1";
        var plan = TrayApplication.BuildScenarioApplyPlan(p, isAcOnline: true, turboSupported: true);
        Assert.Equal(ScenarioFanAction.Curve, plan.FanAction);
        Assert.Equal("Custom 1", plan.FanCurvePreset);
    }
}
