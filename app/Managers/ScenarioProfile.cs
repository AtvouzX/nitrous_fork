using System;
using System.Collections.Generic;
using Nitrous.Enums;

namespace Nitrous.Managers;

/// <summary>
/// One named Scenario: a bundle of settings applied as a unit by the Tray.
/// Display/edit model only; hardware application lives in TrayApplication.Scenarios.
/// </summary>
public sealed class ScenarioProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    // D5: per-Scenario AC/DC power mode pair.
    public PowerProfile AcMode { get; set; } = PowerProfile.Balanced;
    public PowerProfile DcMode { get; set; } = PowerProfile.Balanced;

    // D3: fan mode + custom fixed speeds + fan-curve preset link ("Auto" = follow power mode).
    public FanProfile FanMode { get; set; } = FanProfile.Auto;
    public int CustomFanSpeedCpu { get; set; } = 50;
    public int CustomFanSpeedGpu { get; set; } = 50;
    public string FanCurvePreset { get; set; } = "Auto";

    // D4: keyboard timeout + lighting effect/zone colors. Brightness level is not a Scenario field.
    public bool BacklightTimeout { get; set; } = true;
    public KeyboardEffect LightingEffect { get; set; } = KeyboardEffect.Static;
    public List<string> ZoneColors { get; set; } = ["#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF"];

    // App Sync links: full exe paths; matching uses file name only. Exclusive across profiles.
    public List<string> LinkedApps { get; set; } = [];
    public bool AppSyncEnabled { get; set; } = true;

    public ScenarioProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        AcMode = AcMode,
        DcMode = DcMode,
        FanMode = FanMode,
        CustomFanSpeedCpu = CustomFanSpeedCpu,
        CustomFanSpeedGpu = CustomFanSpeedGpu,
        FanCurvePreset = FanCurvePreset,
        BacklightTimeout = BacklightTimeout,
        LightingEffect = LightingEffect,
        ZoneColors = [.. ZoneColors],
        LinkedApps = [.. LinkedApps],
        AppSyncEnabled = AppSyncEnabled,
    };
}
