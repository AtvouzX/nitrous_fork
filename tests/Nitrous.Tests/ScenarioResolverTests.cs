using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class ScenarioResolverTests
{
    private static ScenarioProfile Profile(string id, string name, params string[] apps) => new()
    {
        Id = id,
        Name = name,
        AcMode = PowerProfile.Performance,
        DcMode = PowerProfile.Quiet,
        LinkedApps = [.. apps],
        AppSyncEnabled = true,
    };

    private static readonly List<ScenarioProfile> Profiles =
    [
        Profile("home", "Daily", @"C:\Apps\mail.exe"),
        Profile("game", "Gaming", @"C:\Games\game.exe"),
        Profile("off", "Disabled", @"C:\Games\other.exe"),
    ];

    static ScenarioResolverTests()
    {
        Profiles[2].AppSyncEnabled = false;
    }

    [Fact]
    public void EffectiveMode_PicksAcOrDc_AndFallsBackFromUnsupportedTurbo()
    {
        var p = Profile("x", "X");
        p.AcMode = PowerProfile.Turbo;
        p.DcMode = PowerProfile.Quiet;

        Assert.Equal(PowerProfile.Turbo, ScenarioResolver.EffectiveMode(p, isAcOnline: true, turboSupported: true));
        Assert.Equal(PowerProfile.Performance, ScenarioResolver.EffectiveMode(p, isAcOnline: true, turboSupported: false));
        Assert.Equal(PowerProfile.Quiet, ScenarioResolver.EffectiveMode(p, isAcOnline: false, turboSupported: false));
    }

    [Fact]
    public void FindLinkedProfile_ReturnsOwningProfileForLinkedExe()
    {
        var profile = ScenarioResolver.FindLinkedProfile(Profiles, "GAME.EXE");

        Assert.NotNull(profile);
        Assert.Equal("game", profile.Id);
    }

    [Fact]
    public void FindLinkedProfile_UnlinkedExe_ReturnsNull()
    {
        Assert.Null(ScenarioResolver.FindLinkedProfile(Profiles, "browser.exe"));
        Assert.Null(ScenarioResolver.FindLinkedProfile(Profiles, null));
        Assert.Null(ScenarioResolver.FindLinkedProfile(Profiles, "  "));
    }

    [Fact]
    public void FindLinkedProfile_DisabledProfile_ReturnsNull()
    {
        Assert.Null(ScenarioResolver.FindLinkedProfile(Profiles, "other.exe"));
    }

    [Fact]
    public void ResolveForeground_Match_SwitchesToLinkedProfile()
    {
        var r = ScenarioResolver.ResolveForeground(Profiles, activeId: "home", selectedId: "home", exeFileName: "GAME.EXE", masterEnabled: true);
        Assert.Equal(ScenarioSwitchReason.Match, r.Reason);
        Assert.Equal("game", r.TargetActiveId);
    }

    [Fact]
    public void ResolveForeground_MatchAlreadyActive_Keeps()
    {
        var r = ScenarioResolver.ResolveForeground(Profiles, activeId: "game", selectedId: "home", exeFileName: "game.exe", masterEnabled: true);
        Assert.Equal(ScenarioSwitchReason.Keep, r.Reason);
        Assert.Equal("game", r.TargetActiveId);
    }

    [Fact]
    public void ResolveForeground_NoMatch_KeepsActive_NoRevert()
    {
        var r = ScenarioResolver.ResolveForeground(Profiles, activeId: "game", selectedId: "home", exeFileName: "browser.exe", masterEnabled: true);
        Assert.Equal(ScenarioSwitchReason.Keep, r.Reason);
        Assert.Equal("game", r.TargetActiveId);
    }

    [Fact]
    public void ResolveForeground_NoMatchAlreadyOnSelected_Keeps()
    {
        var r = ScenarioResolver.ResolveForeground(Profiles, activeId: "home", selectedId: "home", exeFileName: "browser.exe", masterEnabled: true);
        Assert.Equal(ScenarioSwitchReason.Keep, r.Reason);
        Assert.Equal("home", r.TargetActiveId);
    }

    [Fact]
    public void ResolveForeground_DisabledProfileLink_NeverMatches()
    {
        var r = ScenarioResolver.ResolveForeground(Profiles, activeId: "home", selectedId: "home", exeFileName: "other.exe", masterEnabled: true);
        Assert.Equal(ScenarioSwitchReason.Keep, r.Reason);
        Assert.Equal("home", r.TargetActiveId);
    }

    [Fact]
    public void ResolveForeground_MasterOffOrNoExe_Keeps()
    {
        Assert.Equal(ScenarioSwitchReason.Keep,
            ScenarioResolver.ResolveForeground(Profiles, "home", "home", "game.exe", masterEnabled: false).Reason);
        Assert.Equal(ScenarioSwitchReason.Keep,
            ScenarioResolver.ResolveForeground(Profiles, "game", "home", null, masterEnabled: true).Reason);
        Assert.Equal(ScenarioSwitchReason.Keep,
            ScenarioResolver.ResolveForeground(Profiles, "game", "home", "  ", masterEnabled: true).Reason);
    }

    [Fact]
    public void ResolveAppExit_WhenSyncProfileActive_RevertsToSelected()
    {
        var r = ScenarioResolver.ResolveAppExit(Profiles, activeId: "game", selectedId: "home", appSyncActiveId: "game");
        Assert.Equal(ScenarioSwitchReason.Revert, r.Reason);
        Assert.Equal("home", r.TargetActiveId);
    }

    [Fact]
    public void ResolveAppExit_WhenUserManuallyApplied_KeepsUserChoice()
    {
        var r = ScenarioResolver.ResolveAppExit(Profiles, activeId: "home", selectedId: "home", appSyncActiveId: "game");
        Assert.Equal(ScenarioSwitchReason.Keep, r.Reason);
        Assert.Equal("home", r.TargetActiveId);
    }

    [Fact]
    public void ResolveAppExit_NoSyncActive_Keeps()
    {
        var r = ScenarioResolver.ResolveAppExit(Profiles, activeId: "game", selectedId: "home", appSyncActiveId: null);
        Assert.Equal(ScenarioSwitchReason.Keep, r.Reason);
        Assert.Equal("game", r.TargetActiveId);
    }

    [Fact]
    public void FindRunningLinkedProfile_PicksFirstEnabledRunning_SkipsExcludedAndDisabled()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "game.exe", "other.exe" };
        bool IsRunning(string exe) => running.Contains(exe);

        // "game" excluded (it just exited), "off" disabled despite running -> no fallback here.
        var onlyDisabled = ScenarioResolver.FindRunningLinkedProfile(Profiles, excludeProfileId: "game", IsRunning);
        Assert.Null(onlyDisabled);

        running.Add("mail.exe");
        var fallback = ScenarioResolver.FindRunningLinkedProfile(Profiles, excludeProfileId: "game", IsRunning);
        Assert.NotNull(fallback);
        Assert.Equal("home", fallback.Value.Profile.Id);
        Assert.Equal("mail.exe", fallback.Value.ExeFileName);
    }

    [Fact]
    public void ResolveForeground_UnknownActive_FallsBackToSelected()
    {
        var r = ScenarioResolver.ResolveForeground(Profiles, activeId: "ghost", selectedId: "home", exeFileName: "browser.exe", masterEnabled: true);
        Assert.Equal("home", r.TargetActiveId);
    }
}
