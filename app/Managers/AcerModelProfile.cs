using System;
using System.Collections.Generic;

namespace Nitrous.Managers;

/// <summary>
/// Per-model hardware profile overrides for quirks that cannot be detected dynamically.
/// </summary>
internal sealed record AcerModelProfile(string? ModelCode, bool RequiresBalancedModeReset)
{
    public static AcerModelProfile Unknown { get; } = new(null, RequiresBalancedModeReset: false);

    private static readonly Dictionary<string, AcerModelProfile> Models =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AN16S-61"] = new("AN16S-61", RequiresBalancedModeReset: false)
        };

    public static AcerModelProfile For(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return Unknown;

        foreach (var (code, profile) in Models)
        {
            if (ContainsCode(model, code)) return profile;
        }

        return Unknown;
    }

    private static bool ContainsCode(string model, string code)
    {
        var at = model.IndexOf(code, StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            var before = at == 0 || !char.IsLetterOrDigit(model[at - 1]);
            var end = at + code.Length;
            var after = end >= model.Length || !char.IsLetterOrDigit(model[end]);
            if (before && after) return true;

            at = model.IndexOf(code, at + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
