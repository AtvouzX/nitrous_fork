using System;
using System.Collections.Generic;
using System.Linq;
using Nitrous.Enums;
using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

public class ScenarioDashboardTests : IDisposable
{
    private static readonly string[] Keys =
    [
        ScenarioStore.ProfilesKey, ScenarioStore.ActiveIdKey, ScenarioStore.SelectedIdKey,
        ScenarioStore.ApplyRequestKey, ScenarioStore.EnabledKey,
    ];

    private readonly Dictionary<string, object?> _backup = new();

    public ScenarioDashboardTests()
    {
        foreach (string key in Keys)
        {
            _backup[key] = SettingsManager.Get(key, (object?)null);
            SettingsManager.Delete(key);
        }
        SettingsManager.InvalidateCache();
    }

    public void Dispose()
    {
        foreach (string key in Keys)
        {
            if (_backup[key] is { } value) SettingsManager.Save(key, value);
            else SettingsManager.Delete(key);
        }
        SettingsManager.InvalidateCache();
    }

    [Fact]
    public void ViewModel_LoadsSeededScenarios_SelectsActive()
    {
        using var vm = new DashboardViewModel();

        Assert.Equal(3, vm.ScenarioProfiles.Count);
        Assert.NotNull(vm.SelectedScenario);
        Assert.Equal(vm.ScenarioProfiles[0].Id, vm.SelectedScenario!.Id);
        Assert.False(vm.ScenarioEnabled);
    }

    [Fact]
    public void ReloadScenarios_UnchangedPayload_KeepsLoadedObjects()
    {
        using var vm = new DashboardViewModel();
        var selectedBefore = vm.SelectedScenario;

        vm.ReloadScenarios();

        Assert.Same(selectedBefore, vm.SelectedScenario);
    }

    [Fact]
    public void Create_AddsPersistedProfile_AndSelectsIt()
    {
        using var vm = new DashboardViewModel();
        int before = vm.ScenarioProfiles.Count;

        vm.CreateScenarioCommand.Execute(null);

        Assert.Equal(before + 1, vm.ScenarioProfiles.Count);
        Assert.Equal(before + 1, ScenarioStore.LoadProfiles().Count);
        Assert.Equal(vm.ScenarioProfiles[^1].Id, vm.SelectedScenario!.Id);
    }

    [Fact]
    public void Duplicate_ClearsLinkedApps_AndApplyActivatesSelected()
    {
        using var vm = new DashboardViewModel();
        var source = vm.ScenarioProfiles[0];
        source.LinkedApps = [@"C:\Games\game.exe"];
        vm.SaveScenarios();
        vm.SelectedScenario = source;

        vm.DuplicateScenarioCommand.Execute(null);
        var copy = vm.SelectedScenario!;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Empty(copy.LinkedApps);

        int requestBefore = ScenarioStore.GetApplyRequest();
        vm.ApplyScenarioCommand.Execute(null);

        Assert.True(vm.ScenarioEnabled);
        Assert.Equal(copy.Id, ScenarioStore.GetActiveId());
        Assert.Equal(copy.Id, ScenarioStore.GetSelectedId());
        Assert.True(ScenarioStore.GetApplyRequest() > requestBefore);
    }

    [Fact]
    public void Delete_RemovesProfile_NeverDeletesLastOne()
    {
        using var vm = new DashboardViewModel();
        vm.CreateScenarioCommand.Execute(null);
        Assert.Equal(4, vm.ScenarioProfiles.Count);

        vm.DeleteScenarioCommand.Execute(null);
        Assert.Equal(3, vm.ScenarioProfiles.Count);
        Assert.Equal(3, ScenarioStore.LoadProfiles().Count);

        while (vm.ScenarioProfiles.Count > 1)
        {
            vm.SelectedScenario = vm.ScenarioProfiles[0];
            vm.DeleteScenarioCommand.Execute(null);
        }
        vm.DeleteScenarioCommand.Execute(null);
        Assert.Single(vm.ScenarioProfiles);
    }

    [Fact]
    public void Dashboard_PowerCardHasScenarioManagerButton_NoScenarioNavTab()
    {
        Exception? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
                var window = new NitrousDashboard();
                Assert.Null(window.FindName("NavScenarioBtn"));
                Assert.Null(window.FindName("ScenariosPage"));

                var dashPage = (System.Windows.FrameworkElement)window.FindName("DashPage")!;
                Assert.NotNull(dashPage.FindName("BtnScenarioManager"));
                window.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Dashboard_ScenarioManager_UsesManualDockedPosition()
    {
        Exception? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
                var dashboard = new NitrousDashboard();
                dashboard.Left = 1200;
                dashboard.Top = 200;

                var manager = new Nitrous.Ui.ScenariosWindow((DashboardViewModel)dashboard.DataContext);
                dashboard.PositionCompanionWindow(manager);

                Assert.Equal(System.Windows.WindowStartupLocation.Manual, manager.WindowStartupLocation);
                Assert.False(double.IsNaN(manager.Left));
                Assert.False(double.IsNaN(manager.Top));
                Assert.True(manager.Left < dashboard.Left || manager.Left == 0,
                    "Scenario Manager should dock left of the dashboard (or clamp to the work area).");

                manager.Close();
                dashboard.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void ScenarioManagerWindow_OpensWithProfileList()
    {
        Exception? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
                using var vm = new DashboardViewModel();
                var window = new Nitrous.Ui.ScenariosWindow(vm);
                Assert.NotNull(window.FindName("ScenarioList"));
                Assert.NotNull(window.FindName("ScenarioAppList"));
                window.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void FanControlKind_MapsToFanModeAndCurvePreset()
    {
        using var vm = new DashboardViewModel();
        vm.SelectedScenario = vm.ScenarioProfiles[0];

        vm.ScenarioFanControlKind = "Curve";
        Assert.Equal(FanProfile.Medium, vm.SelectedScenario!.FanMode);
        Assert.NotEqual("Auto", vm.SelectedScenario.FanCurvePreset);
        Assert.True(vm.IsScenarioCurveFan);
        Assert.False(vm.IsScenarioFixedFan);

        vm.ScenarioFanControlKind = "Fixed Speed";
        Assert.Equal(FanProfile.Medium, vm.SelectedScenario.FanMode);
        Assert.Equal("Auto", vm.SelectedScenario.FanCurvePreset);
        Assert.True(vm.IsScenarioFixedFan);
        Assert.False(vm.IsScenarioCurveFan);

        vm.ScenarioFanControlKind = "Max";
        Assert.Equal(FanProfile.Max, vm.SelectedScenario.FanMode);
        Assert.Equal("Auto", vm.SelectedScenario.FanCurvePreset);
        Assert.False(vm.IsScenarioFixedFan);
        Assert.False(vm.IsScenarioCurveFan);
    }

    [Fact]
    public void AddLinkedApp_PopulatesDisplayItemsWithAppName()
    {
        using var vm = new DashboardViewModel();
        vm.SelectedScenario = vm.ScenarioProfiles[0];

        Assert.True(vm.AddLinkedApp(@"C:\Games\Foo\game.exe"));

        var item = Assert.Single(vm.ScenarioLinkedApps);
        Assert.Equal("game", item.DisplayName);
        Assert.Equal(@"C:\Games\Foo\game.exe", item.Path);
    }

    [Fact]
    public void FanProxies_ClampAndPersistWithCurvePreset()
    {
        using var vm = new DashboardViewModel();
        vm.SelectedScenario = vm.ScenarioProfiles[0];

        vm.ScenarioFanSpeedCpu = 140;
        vm.ScenarioFanSpeedGpu = -3;
        vm.ScenarioCurvePreset = "Custom 1";
        vm.SaveScenarioCommand.Execute(null);

        var saved = ScenarioStore.LoadProfiles()[0];
        Assert.Equal(100, saved.CustomFanSpeedCpu);
        Assert.Equal(0, saved.CustomFanSpeedGpu);
        Assert.Equal("Custom 1", saved.FanCurvePreset);
    }

    [Fact]
    public void AddLinkedApp_IsExclusiveAcrossProfiles_AndRemoveWorks()
    {
        using var vm = new DashboardViewModel();
        var first = vm.ScenarioProfiles[0];
        var second = vm.ScenarioProfiles[1];

        vm.SelectedScenario = first;
        Assert.True(vm.AddLinkedApp(@"C:\Games\Foo\game.exe"));
        Assert.Single(ScenarioStore.LoadProfiles()[0].LinkedApps);

        second = vm.ScenarioProfiles[1];
        vm.SelectedScenario = second;
        Assert.True(vm.AddLinkedApp(@"D:\Other\game.exe")); // same file name: link moves
        Assert.Empty(ScenarioStore.LoadProfiles()[0].LinkedApps);
        Assert.Single(ScenarioStore.LoadProfiles()[1].LinkedApps);

        vm.RemoveLinkedApp(@"D:\Other\game.exe");
        Assert.Empty(ScenarioStore.LoadProfiles()[1].LinkedApps);
    }

    [Fact]
    public void KeyboardProxies_PersistTimeoutEffectAndZoneColors()
    {
        using var vm = new DashboardViewModel();
        vm.SelectedScenario = vm.ScenarioProfiles[0];

        vm.ScenarioBacklightTimeout = false;
        vm.ScenarioLightingEffect = KeyboardEffect.Breathing;
        vm.ScenarioZoneColor2 = "#00FF00";
        vm.SaveScenarioCommand.Execute(null);

        var saved = ScenarioStore.LoadProfiles()[0];
        Assert.False(saved.BacklightTimeout);
        Assert.Equal(KeyboardEffect.Breathing, saved.LightingEffect);
        Assert.Equal("#00FF00", saved.ZoneColors[1]);
    }

    [Fact]
    public void EditorProxies_WriteIntoSelectedProfile_AndSavePersists()
    {
        using var vm = new DashboardViewModel();
        vm.SelectedScenario = vm.ScenarioProfiles[0];

        vm.ScenarioName = "Work Focus";
        vm.ScenarioAcMode = PowerProfile.Quiet;
        vm.ScenarioDcMode = PowerProfile.Balanced;
        vm.ScenarioFanMode = FanProfile.Max;
        vm.SaveScenarioCommand.Execute(null);

        var saved = ScenarioStore.LoadProfiles()[0];
        Assert.Equal("Work Focus", saved.Name);
        Assert.Equal(PowerProfile.Quiet, saved.AcMode);
        Assert.Equal(PowerProfile.Balanced, saved.DcMode);
        Assert.Equal(FanProfile.Max, saved.FanMode);
    }
}
