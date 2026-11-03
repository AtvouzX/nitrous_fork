using System;
using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Helpers;

namespace Nitrous.Managers;

/// <summary>
/// Registry persistence for Scenarios. Thin wrapper over <see cref="SettingsManager"/>;
/// all profile data passes through <see cref="ScenarioProfileCodec"/> sanitization.
/// </summary>
public static class ScenarioStore
{
    public const string ProfilesKey = "Scenarios_v1";
    public const string ActiveIdKey = "ScenarioActiveId";
    public const string SelectedIdKey = "ScenarioSelectedId";
    public const string ApplyRequestKey = "ScenarioApplyRequest";
    public const string EnabledKey = "ScenarioEnabled";
    public const string SuspendedKey = "ScenarioSuspended";

    public static List<ScenarioProfile> LoadProfiles()
    {
        return ScenarioProfileCodec.Deserialize(SettingsManager.GetString(ProfilesKey, string.Empty));
    }

    public static List<ScenarioProfile> SaveProfiles(IReadOnlyList<ScenarioProfile> profiles)
    {
        var clean = ScenarioProfileCodec.Sanitize(profiles);
        SettingsManager.Save(ProfilesKey, ScenarioProfileCodec.Serialize(clean));
        return clean;
    }

    public static bool IsEnabled => SettingsManager.GetInt(EnabledKey, 0) == 1;

    public static void SetEnabled(bool enabled) => SettingsManager.Save(EnabledKey, enabled ? 1 : 0);

    /// <summary>
    /// Manual Dashboard/Tray profile change suspends Scenario assertion:
    /// Tray will not re-apply the active Scenario on power-source changes
    /// until the next explicit Scenario apply (Dashboard, Tray menu, App Sync, boot).
    /// </summary>
    public static bool IsSuspended => SettingsManager.GetInt(SuspendedKey, 0) == 1;

    public static void SetSuspended(bool suspended) => SettingsManager.Save(SuspendedKey, suspended ? 1 : 0);

    public static string GetActiveId() => SettingsManager.GetString(ActiveIdKey, string.Empty);

    public static void SetActiveId(string id) => SettingsManager.Save(ActiveIdKey, id ?? string.Empty);

    public static string GetSelectedId() => SettingsManager.GetString(SelectedIdKey, string.Empty);

    public static void SetSelectedId(string id) => SettingsManager.Save(SelectedIdKey, id ?? string.Empty);

    public static int GetApplyRequest() => SettingsManager.GetInt(ApplyRequestKey, 0);

    public static int RequestApply()
    {
        int next = GetApplyRequest() + 1;
        SettingsManager.Save(ApplyRequestKey, next);
        return next;
    }

    /// <summary>
    /// First-run seed: three presets, Daily Use snapshots current settings so an
    /// upgrade changes nothing. Master switch stays off until the user opts in.
    /// </summary>
    public static List<ScenarioProfile> SeedIfAbsent()
    {
        if (SettingsManager.Get(ProfilesKey, (string?)null) != null)
        {
            return LoadProfiles();
        }

        var saved = SaveProfiles(CreateSeedProfiles());
        if (saved.Count > 0)
        {
            SetActiveId(saved[0].Id);
            SetSelectedId(saved[0].Id);
        }
        LogManager.Info("Seeded default Scenario profiles", "Scenario");
        return saved;
    }

    /// <summary>Fresh preset templates (new IDs) used by seeding and Restore.</summary>
    public static List<ScenarioProfile> CreateSeedProfiles()
    {
        var currentMode = (PowerProfile)SettingsManager.GetInt("LastPowerMode", (int)PowerProfile.Performance);
        if (!Enum.IsDefined(currentMode)) currentMode = PowerProfile.Balanced;
        var currentFan = Enum.TryParse(SettingsManager.GetString("LastFanMode", "Auto"), out FanProfile fan)
            ? fan
            : FanProfile.Auto;

        var daily = new ScenarioProfile
        {
            Name = "Daily Use",
            AcMode = currentMode,
            DcMode = currentMode,
            FanMode = currentFan,
            CustomFanSpeedCpu = SettingsManager.GetInt("CustomFanSpeedCpu", 50),
            CustomFanSpeedGpu = SettingsManager.GetInt("CustomFanSpeedGpu", 50),
            FanCurvePreset = SettingsManager.GetString("ActiveFanCurvePreset", "Auto"),
            BacklightTimeout = SettingsManager.Get("KeyboardBacklightTimeout", true),
            LightingEffect = ParseEffect(SettingsManager.GetString("KbEffect", "Static")),
            ZoneColors = LoadZoneColors(),
        };

        var gaming = new ScenarioProfile
        {
            Name = "Gaming",
            AcMode = PowerProfile.Turbo, // gated by IsTurboModeSupported at apply time
            DcMode = PowerProfile.Balanced,
            FanMode = FanProfile.Auto,
            FanCurvePreset = "Auto",
            BacklightTimeout = true,
            LightingEffect = KeyboardEffect.Wave,
        };

        var quiet = new ScenarioProfile
        {
            Name = "Quiet Occasion",
            AcMode = PowerProfile.Quiet,
            DcMode = PowerProfile.Quiet,
            FanMode = FanProfile.Auto,
            FanCurvePreset = "Auto",
            BacklightTimeout = true,
            LightingEffect = KeyboardEffect.Static,
        };

        return [daily, gaming, quiet];
    }

    /// <summary>
    /// Mirrors an applied Scenario into the legacy single-value keys so existing
    /// readers (Dashboard fast load, Tray boot restore, ThermalEngine) stay coherent.
    /// </summary>
    public static void WriteThroughLegacy(ScenarioProfile profile, PowerProfile effectiveMode, bool isAcOnline)
    {
        SettingsManager.Save("LastPowerMode", (int)effectiveMode);
        SettingsManager.Save(isAcOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)effectiveMode);
        SettingsManager.Save("LastFanMode", profile.FanMode.ToString());
        SettingsManager.Save("CustomFanSpeedCpu", profile.CustomFanSpeedCpu);
        SettingsManager.Save("CustomFanSpeedGpu", profile.CustomFanSpeedGpu);
        SettingsManager.Save("ActiveFanCurvePreset", profile.FanCurvePreset);
        // A named curve preset means curve mode; "Auto" means fixed/auto fan per FanMode.
        SettingsManager.Save("IsCurveModeEnabled", profile.FanCurvePreset != "Auto" ? 1 : 0);
        SettingsManager.Save("KeyboardBacklightTimeout", profile.BacklightTimeout);
        SettingsManager.Save("KbEffect", profile.LightingEffect.ToString());
        for (int i = 0; i < profile.ZoneColors.Count && i < 4; i++)
        {
            SettingsManager.Save($"KbZone{i + 1}", profile.ZoneColors[i]);
        }
    }

    private static KeyboardEffect ParseEffect(string? name)
    {
        return Enum.TryParse(name, ignoreCase: true, out KeyboardEffect effect) && Enum.IsDefined(effect)
            ? effect
            : KeyboardEffect.Static;
    }

    private static List<string> LoadZoneColors()
    {
        var colors = new List<string>(4);
        for (int i = 1; i <= 4; i++)
        {
            colors.Add(SettingsManager.GetString($"KbZone{i}", "#FFFFFF"));
        }
        return colors;
    }
}
