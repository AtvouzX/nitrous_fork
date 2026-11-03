using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nitrous.Enums;
using Nitrous.Managers;

namespace Nitrous.Helpers;

/// <summary>
/// Pure encode/decode/sanitize for Scenario persistence. No I/O, no hardware.
/// Stored form is one JSON registry string; every loaded profile is sanitized
/// so corrupt or hand-edited data can never reach hardware apply paths.
/// </summary>
public static class ScenarioProfileCodec
{
    public const int MaxProfiles = 15;
    public const int MaxLinkedApps = 12;
    public const int MaxNameLength = 64;
    public const string DefaultZoneColor = "#FFFFFF";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly char[] InvalidPresetChars = [';', ',', '\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    public static string Serialize(IReadOnlyList<ScenarioProfile> profiles)
    {
        return JsonSerializer.Serialize(Sanitize(profiles), JsonOptions);
    }

    public static List<ScenarioProfile> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var profiles = JsonSerializer.Deserialize<List<ScenarioProfile>>(json, JsonOptions);
            return profiles == null ? [] : Sanitize(profiles);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static List<ScenarioProfile> Sanitize(IReadOnlyList<ScenarioProfile>? profiles)
    {
        var result = new List<ScenarioProfile>();
        if (profiles == null) return result;

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in profiles)
        {
            if (result.Count >= MaxProfiles) break;
            if (raw == null) continue;

            var p = raw.Clone();

            if (string.IsNullOrWhiteSpace(p.Id) || !usedIds.Add(p.Id))
            {
                p.Id = Guid.NewGuid().ToString("N");
                usedIds.Add(p.Id);
            }

            p.Name = SanitizeName(p.Name, result.Count + 1, usedNames);
            p.AcMode = Enum.IsDefined(p.AcMode) ? p.AcMode : PowerProfile.Balanced;
            p.DcMode = Enum.IsDefined(p.DcMode) ? p.DcMode : PowerProfile.Balanced;
            p.FanMode = Enum.IsDefined(p.FanMode) ? p.FanMode : FanProfile.Auto;
            p.CustomFanSpeedCpu = Math.Clamp(p.CustomFanSpeedCpu, 0, 100);
            p.CustomFanSpeedGpu = Math.Clamp(p.CustomFanSpeedGpu, 0, 100);
            p.FanCurvePreset = SanitizePreset(p.FanCurvePreset);
            p.LightingEffect = Enum.IsDefined(p.LightingEffect) ? p.LightingEffect : KeyboardEffect.Static;
            p.ZoneColors = SanitizeZoneColors(p.ZoneColors);
            p.LinkedApps = SanitizeLinkedApps(p.LinkedApps, claimedApps);

            result.Add(p);
        }

        return result;
    }

    private static string SanitizeName(string? name, int fallbackIndex, HashSet<string> usedNames)
    {
        string clean = (name ?? string.Empty).Trim();
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].TrimEnd();
        if (clean.Length == 0) clean = $"Scenario {fallbackIndex}";

        string candidate = clean;
        for (int n = 2; !usedNames.Add(candidate); n++)
        {
            candidate = $"{clean} ({n})";
        }
        return candidate;
    }

    private static string SanitizePreset(string? preset)
    {
        string clean = (preset ?? string.Empty).Trim();
        foreach (char c in InvalidPresetChars) clean = clean.Replace(c.ToString(), string.Empty);
        clean = clean.Trim();
        if (clean.Length > 32) clean = clean[..32].TrimEnd();
        return clean.Length == 0 ? "Auto" : clean;
    }

    private static List<string> SanitizeZoneColors(List<string>? colors)
    {
        var result = new List<string>(4);
        if (colors != null)
        {
            foreach (string color in colors)
            {
                if (result.Count >= 4) break;
                result.Add(NormalizeColor(color));
            }
        }
        while (result.Count < 4) result.Add(DefaultZoneColor);
        return result;
    }

    private static string NormalizeColor(string? color)
    {
        string hex = (color ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length != 6) return DefaultZoneColor;
        foreach (char c in hex)
        {
            bool isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex) return DefaultZoneColor;
        }
        return "#" + hex.ToUpperInvariant();
    }

    private static List<string> SanitizeLinkedApps(List<string>? apps, HashSet<string> claimedApps)
    {
        var result = new List<string>();
        if (apps == null) return result;

        foreach (string app in apps)
        {
            if (result.Count >= MaxLinkedApps) break;
            string path = (app ?? string.Empty).Trim();
            if (path.Length == 0) continue;

            string fileName = Path.GetFileName(path);
            if (fileName.Length == 0) continue;
            if (!claimedApps.Add(fileName)) continue; // exclusive: first profile wins

            result.Add(path);
        }
        return result;
    }
}
