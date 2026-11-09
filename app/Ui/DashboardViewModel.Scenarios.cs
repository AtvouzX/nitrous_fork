using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Mvvm;

namespace Nitrous.Ui;

/// <summary>App Sync list row: app icon + display name; full path kept for matching/tooltip.</summary>
public sealed class ScenarioAppItem
{
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public ImageSource? Icon { get; init; }
}

public partial class DashboardViewModel
{
    private readonly ObservableCollection<ScenarioProfile> _scenarioProfiles = [];
    private ScenarioProfile? _selectedScenario;
    private bool _scenarioEnabled;
    private string _activeScenarioId = string.Empty;
    private string? _lastScenarioPayload;

    public ObservableCollection<ScenarioProfile> ScenarioProfiles => _scenarioProfiles;

    public ObservableCollection<PowerProfile> ScenarioAcPowerModes { get; } = [];
    public ObservableCollection<PowerProfile> ScenarioDcPowerModes { get; } = [];
    public ObservableCollection<PowerProfile> ScenarioPowerModes => ScenarioAcPowerModes;

    /// <summary>False on single-color / unreachable keyboards: RGB effect and zone editors hide.</summary>
    public bool ScenarioRgbSupported { get; private set; }

    public IReadOnlyList<FanProfile> ScenarioFanModes { get; } =
        [FanProfile.Auto, FanProfile.Quiet, FanProfile.Medium, FanProfile.Max];

    public IReadOnlyList<KeyboardEffect> ScenarioEffects { get; } =
        [KeyboardEffect.Static, KeyboardEffect.Breathing, KeyboardEffect.Neon, KeyboardEffect.Wave,
         KeyboardEffect.Shifting, KeyboardEffect.Zoom, KeyboardEffect.Meteor, KeyboardEffect.Twinkling];

    public ObservableCollection<string> ScenarioCurvePresets { get; } = ["Auto"];
    public ObservableCollection<ScenarioAppItem> ScenarioLinkedApps { get; } = [];

    public IReadOnlyList<string> ScenarioFanControlKinds { get; } = ["Auto", "Quiet", "Max", "Fixed Speed", "Curve"];

    /// <summary>
    /// Single fan control choice shown to the user. Maps onto FanMode + FanCurvePreset:
    /// Auto/Quiet/Max are EC preset modes, Fixed Speed locks custom percents,
    /// Curve follows a temperature fan-curve preset.
    /// </summary>
    public string ScenarioFanControlKind
    {
        get
        {
            if (_selectedScenario == null) return "Auto";
            if (_selectedScenario.FanCurvePreset != "Auto") return "Curve";
            return _selectedScenario.FanMode switch
            {
                FanProfile.Medium => "Fixed Speed",
                FanProfile.Quiet => "Quiet",
                FanProfile.Max => "Max",
                _ => "Auto",
            };
        }
        set
        {
            if (_selectedScenario == null) return;
            switch (value)
            {
                case "Quiet":
                    _selectedScenario.FanMode = FanProfile.Quiet;
                    _selectedScenario.FanCurvePreset = "Auto";
                    break;
                case "Max":
                    _selectedScenario.FanMode = FanProfile.Max;
                    _selectedScenario.FanCurvePreset = "Auto";
                    break;
                case "Fixed Speed":
                    _selectedScenario.FanMode = FanProfile.Medium;
                    _selectedScenario.FanCurvePreset = "Auto";
                    break;
                case "Curve":
                    _selectedScenario.FanMode = FanProfile.Medium;
                    if (_selectedScenario.FanCurvePreset == "Auto")
                    {
                        _selectedScenario.FanCurvePreset = ScenarioCurvePresets.Count > 2 ? ScenarioCurvePresets[2] : "Balanced";
                    }
                    break;
                default:
                    _selectedScenario.FanMode = FanProfile.Auto;
                    _selectedScenario.FanCurvePreset = "Auto";
                    break;
            }
            NotifyFanControlChanged();
        }
    }

    public bool IsScenarioFixedFan => ScenarioFanControlKind == "Fixed Speed";
    public bool IsScenarioCurveFan => ScenarioFanControlKind == "Curve";

    private void NotifyFanControlChanged()
    {
        OnPropertyChanged(nameof(ScenarioFanControlKind));
        OnPropertyChanged(nameof(IsScenarioFixedFan));
        OnPropertyChanged(nameof(IsScenarioCurveFan));
        OnPropertyChanged(nameof(ScenarioFanMode));
        OnPropertyChanged(nameof(ScenarioCurvePreset));
    }

    public ICommand CreateScenarioCommand { get; private set; } = null!;
    public ICommand DuplicateScenarioCommand { get; private set; } = null!;
    public ICommand DeleteScenarioCommand { get; private set; } = null!;
    public ICommand SaveScenarioCommand { get; private set; } = null!;
    public ICommand ApplyScenarioCommand { get; private set; } = null!;
    public ICommand RestoreScenarioCommand { get; private set; } = null!;

    private void InitializeScenarios()
    {
        ScenarioAcPowerModes.Clear();
        ScenarioAcPowerModes.Add(PowerProfile.Quiet);
        ScenarioAcPowerModes.Add(PowerProfile.Balanced);
        ScenarioAcPowerModes.Add(PowerProfile.Performance);
        if (IsTurboSupported) ScenarioAcPowerModes.Add(PowerProfile.Turbo);

        ScenarioDcPowerModes.Clear();
        if (IsEcoSupported) ScenarioDcPowerModes.Add(PowerProfile.Eco);
        ScenarioDcPowerModes.Add(PowerProfile.Quiet);
        ScenarioDcPowerModes.Add(PowerProfile.Balanced);
        ScenarioDcPowerModes.Add(PowerProfile.Performance);
        if (IsTurboSupported) ScenarioDcPowerModes.Add(PowerProfile.Turbo);

        try
        {
            ScenarioRgbSupported = AcerKeyboard.IsAvailable()
                                   && AcerCapabilitiesProvider.Current.Smbios.KeyboardColor != 1;
        }
        catch (Exception ex)
        {
            ScenarioRgbSupported = false;
            LogManager.Debug($"Scenario RGB support probe failed: {ex.Message}", "ScenarioVM");
        }

        CreateScenarioCommand = new RelayCommand(_ => RunScenarioAction(CreateScenario), _ => _scenarioProfiles.Count < ScenarioProfileCodec.MaxProfiles);
        DuplicateScenarioCommand = new RelayCommand(_ => RunScenarioAction(DuplicateScenario), _ => _selectedScenario != null && _scenarioProfiles.Count < ScenarioProfileCodec.MaxProfiles);
        DeleteScenarioCommand = new RelayCommand(_ => RunScenarioAction(DeleteScenario), _ => _selectedScenario != null && _scenarioProfiles.Count > 1);
        SaveScenarioCommand = new RelayCommand(_ => RunScenarioAction(SaveScenarios), _ => _selectedScenario != null);
        ApplyScenarioCommand = new RelayCommand(_ => RunScenarioAction(ApplyScenario), _ => _selectedScenario != null);
        RestoreScenarioCommand = new RelayCommand(_ => RunScenarioAction(RestoreScenario), _ => _selectedScenario != null);

        ReloadScenarios();
    }

    public bool ScenarioEnabled
    {
        get => _scenarioEnabled;
        set
        {
            if (!SetProperty(ref _scenarioEnabled, value)) return;
            try { ScenarioStore.SetEnabled(value); }
            catch (Exception ex) { LogManager.Warn($"Scenario enable save failed: {ex.Message}", "ScenarioVM"); }
        }
    }

    public ScenarioProfile? SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (!SetProperty(ref _selectedScenario, value)) return;
            RefreshLinkedApps();
            NotifyScenarioEditorChanged();
        }
    }

    public bool IsSelectedScenarioActive =>
        _selectedScenario != null && _selectedScenario.Id == _activeScenarioId;

    public bool IsScenarioLimitReached => _scenarioProfiles.Count >= ScenarioProfileCodec.MaxProfiles;

    // ---- Editor proxies (write into SelectedScenario; Save persists) ----

    public string ScenarioName
    {
        get => _selectedScenario?.Name ?? string.Empty;
        set { if (_selectedScenario != null) _selectedScenario.Name = value; }
    }

    public PowerProfile ScenarioAcMode
    {
        get => _selectedScenario?.AcMode ?? PowerProfile.Balanced;
        set
        {
            if (_selectedScenario != null)
            {
                _selectedScenario.AcMode = value == PowerProfile.Eco ? PowerProfile.Balanced : value;
            }
        }
    }

    public PowerProfile ScenarioDcMode
    {
        get => _selectedScenario?.DcMode ?? PowerProfile.Balanced;
        set { if (_selectedScenario != null) _selectedScenario.DcMode = value; }
    }

    public FanProfile ScenarioFanMode
    {
        get => _selectedScenario?.FanMode ?? FanProfile.Auto;
        set { if (_selectedScenario != null) { _selectedScenario.FanMode = value; NotifyFanControlChanged(); } }
    }

    public int ScenarioFanSpeedCpu
    {
        get => _selectedScenario?.CustomFanSpeedCpu ?? 50;
        set
        {
            if (_selectedScenario == null) return;
            _selectedScenario.CustomFanSpeedCpu = Math.Clamp(value, 0, 100);
            OnPropertyChanged(nameof(ScenarioFanSpeedCpu));
        }
    }

    public int ScenarioFanSpeedGpu
    {
        get => _selectedScenario?.CustomFanSpeedGpu ?? 50;
        set
        {
            if (_selectedScenario == null) return;
            _selectedScenario.CustomFanSpeedGpu = Math.Clamp(value, 0, 100);
            OnPropertyChanged(nameof(ScenarioFanSpeedGpu));
        }
    }

    public string ScenarioCurvePreset
    {
        get => _selectedScenario?.FanCurvePreset ?? "Auto";
        set { if (_selectedScenario != null) { _selectedScenario.FanCurvePreset = value; NotifyFanControlChanged(); } }
    }

    public bool ScenarioBacklightTimeout
    {
        get => _selectedScenario?.BacklightTimeout ?? true;
        set { if (_selectedScenario != null) _selectedScenario.BacklightTimeout = value; }
    }

    public KeyboardEffect ScenarioLightingEffect
    {
        get => _selectedScenario?.LightingEffect ?? KeyboardEffect.Static;
        set { if (_selectedScenario != null) _selectedScenario.LightingEffect = value; }
    }

    public bool ScenarioAppSyncEnabled
    {
        get => _selectedScenario?.AppSyncEnabled ?? true;
        set { if (_selectedScenario != null) _selectedScenario.AppSyncEnabled = value; }
    }

    public string ScenarioZoneColor1
    {
        get => ZoneColor(0);
        set => SetZoneColor(0, value);
    }

    public string ScenarioZoneColor2
    {
        get => ZoneColor(1);
        set => SetZoneColor(1, value);
    }

    public string ScenarioZoneColor3
    {
        get => ZoneColor(2);
        set => SetZoneColor(2, value);
    }

    public string ScenarioZoneColor4
    {
        get => ZoneColor(3);
        set => SetZoneColor(3, value);
    }

    private string ZoneColor(int index) =>
        _selectedScenario != null && index < _selectedScenario.ZoneColors.Count
            ? _selectedScenario.ZoneColors[index]
            : "#FFFFFF";

    private void SetZoneColor(int index, string value)
    {
        if (_selectedScenario == null) return;
        while (_selectedScenario.ZoneColors.Count <= index) _selectedScenario.ZoneColors.Add("#FFFFFF");
        _selectedScenario.ZoneColors[index] = value;
    }

    // ---- Actions ----

    private void RunScenarioAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { LogManager.Warn($"Scenario action failed: {ex.Message}", "ScenarioVM"); }
    }

    public void ReloadScenarios()
    {
        // Cheap no-change skip: SyncSettings fires on every settings save in the
        // UI process; rebuilding would re-extract app icons and replace the
        // profile object currently being edited.
        string payload = CurrentScenarioPayload();
        if (payload == _lastScenarioPayload && _scenarioProfiles.Count > 0) return;
        _lastScenarioPayload = payload;

        string? keepId = _selectedScenario?.Id;
        var profiles = ScenarioStore.SeedIfAbsent();

        _scenarioProfiles.Clear();
        foreach (var p in profiles) _scenarioProfiles.Add(p);

        _scenarioEnabled = ScenarioStore.IsEnabled;
        OnPropertyChanged(nameof(ScenarioEnabled));
        _activeScenarioId = ScenarioStore.GetActiveId();

        RefreshCurvePresets();
        SelectedScenario = ScenarioResolver.FindProfile(_scenarioProfiles, keepId)
                           ?? ScenarioResolver.FindProfile(_scenarioProfiles, _activeScenarioId)
                           ?? (_scenarioProfiles.Count > 0 ? _scenarioProfiles[0] : null);
        OnPropertyChanged(nameof(IsScenarioLimitReached));
        _lastScenarioPayload = CurrentScenarioPayload(); // seeding may have written keys
    }

    private static string CurrentScenarioPayload() =>
        SettingsManager.GetString(ScenarioStore.ProfilesKey, string.Empty)
        + "|" + ScenarioStore.GetActiveId()
        + "|" + ScenarioStore.GetSelectedId()
        + "|" + ScenarioStore.IsEnabled;

    public void SaveScenarios()
    {
        string? keepId = _selectedScenario?.Id;
        var saved = ScenarioStore.SaveProfiles(_scenarioProfiles);
        _lastScenarioPayload = CurrentScenarioPayload();

        _scenarioProfiles.Clear();
        foreach (var p in saved) _scenarioProfiles.Add(p);
        SelectedScenario = ScenarioResolver.FindProfile(_scenarioProfiles, keepId)
                           ?? (_scenarioProfiles.Count > 0 ? _scenarioProfiles[0] : null);
        OnPropertyChanged(nameof(IsScenarioLimitReached));
    }

    private void CreateScenario()
    {
        var template = _selectedScenario?.Clone() ?? new ScenarioProfile();
        template.Id = Guid.NewGuid().ToString("N");
        template.Name = string.Empty; // sanitizer assigns a unique default name
        template.LinkedApps = [];
        _scenarioProfiles.Add(template);
        SaveScenarios();
        SelectedScenario = ScenarioResolver.FindProfile(_scenarioProfiles, template.Id);
    }

    private void DuplicateScenario()
    {
        if (_selectedScenario == null) return;
        var copy = _selectedScenario.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = _selectedScenario.Name + " Copy";
        copy.LinkedApps = []; // links are exclusive; duplicates start unlinked
        _scenarioProfiles.Add(copy);
        SaveScenarios();
        SelectedScenario = ScenarioResolver.FindProfile(_scenarioProfiles, copy.Id);
    }

    private void DeleteScenario()
    {
        if (_selectedScenario == null || _scenarioProfiles.Count <= 1) return;
        string deletedId = _selectedScenario.Id;
        bool wasActive = deletedId == ScenarioStore.GetActiveId();

        _scenarioProfiles.Remove(_selectedScenario);
        SaveScenarios();

        SelectedScenario = _scenarioProfiles.Count > 0 ? _scenarioProfiles[0] : null;
        if (wasActive && SelectedScenario != null)
        {
            ScenarioStore.SetSelectedId(SelectedScenario.Id);
            ScenarioStore.SetActiveId(SelectedScenario.Id);
            if (ScenarioStore.IsEnabled) ScenarioStore.RequestApply();
            _activeScenarioId = SelectedScenario.Id;
            OnPropertyChanged(nameof(IsSelectedScenarioActive));
        }
    }

    private void ApplyScenario()
    {
        if (_selectedScenario == null) return;
        SaveScenarios();
        var target = ScenarioResolver.FindProfile(_scenarioProfiles, _selectedScenario.Id) ?? _selectedScenario;
        SelectedScenario = target;

        ScenarioStore.SetSelectedId(target.Id);
        ScenarioStore.SetActiveId(target.Id);
        _activeScenarioId = target.Id;
        if (!_scenarioEnabled)
        {
            _scenarioEnabled = true;
            OnPropertyChanged(nameof(ScenarioEnabled));
            ScenarioStore.SetEnabled(true);
        }
        ScenarioStore.RequestApply();
        OnPropertyChanged(nameof(IsSelectedScenarioActive));
        LogManager.Info($"Scenario selected for apply: {target.Name}", "ScenarioVM");
    }

    private void RestoreScenario()
    {
        if (_selectedScenario == null) return;
        var templates = ScenarioStore.CreateSeedProfiles();
        var template = ScenarioResolver.FindProfileByName(templates, _selectedScenario.Name) ?? templates[0];

        string id = _selectedScenario.Id;
        string name = _selectedScenario.Name;
        var restored = template.Clone();
        restored.Id = id;
        restored.Name = name;
        restored.LinkedApps = [];

        int index = _scenarioProfiles.IndexOf(_selectedScenario);
        if (index >= 0) _scenarioProfiles[index] = restored;
        SaveScenarios();
        SelectedScenario = ScenarioResolver.FindProfile(_scenarioProfiles, id);
    }

    // ---- App links (T7) ----

    public bool AddLinkedApp(string? path)
    {
        if (_selectedScenario == null || string.IsNullOrWhiteSpace(path)) return false;
        string fileName = Path.GetFileName(path);
        if (fileName.Length == 0) return false;

        // Exclusive: strip this exe from every other profile.
        foreach (var p in _scenarioProfiles)
        {
            if (p.Id == _selectedScenario.Id) continue;
            p.LinkedApps.RemoveAll(a => string.Equals(Path.GetFileName(a), fileName, StringComparison.OrdinalIgnoreCase));
        }

        _selectedScenario.LinkedApps.RemoveAll(a => string.Equals(Path.GetFileName(a), fileName, StringComparison.OrdinalIgnoreCase));
        if (_selectedScenario.LinkedApps.Count >= ScenarioProfileCodec.MaxLinkedApps) return false;
        _selectedScenario.LinkedApps.Add(path);
        SaveScenarios();
        SelectedScenario = ScenarioResolver.FindProfile(_scenarioProfiles, _selectedScenario?.Id);
        return true;
    }

    public void RemoveLinkedApp(string? path)
    {
        if (_selectedScenario == null || path == null) return;
        _selectedScenario.LinkedApps.RemoveAll(a => string.Equals(a, path, StringComparison.OrdinalIgnoreCase));
        SaveScenarios();
        SelectedScenario = ScenarioResolver.FindProfile(_scenarioProfiles, _selectedScenario?.Id);
    }

    private void RefreshLinkedApps()
    {
        ScenarioLinkedApps.Clear();
        if (_selectedScenario == null) return;
        foreach (string app in _selectedScenario.LinkedApps)
        {
            ScenarioLinkedApps.Add(new ScenarioAppItem
            {
                Path = app,
                DisplayName = Path.GetFileNameWithoutExtension(app),
                Icon = ExtractAppIcon(app),
            });
        }
    }

    private static ImageSource? ExtractAppIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"App icon extraction failed for {Path.GetFileName(path)}: {ex.Message}", "ScenarioVM");
            return null;
        }
    }

    private void RefreshCurvePresets()
    {
        ScenarioCurvePresets.Clear();
        ScenarioCurvePresets.Add("Auto");
        foreach (string preset in new[] { "Quiet", "Balanced", "Performance", "Turbo" }) ScenarioCurvePresets.Add(preset);
        string saved = SettingsManager.GetString("FanCurvePresets", string.Empty);
        foreach (string preset in saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ScenarioCurvePresets.Contains(preset)) ScenarioCurvePresets.Add(preset);
        }
    }

    private void NotifyScenarioEditorChanged()
    {
        OnPropertyChanged(nameof(ScenarioName));
        OnPropertyChanged(nameof(ScenarioAcMode));
        OnPropertyChanged(nameof(ScenarioDcMode));
        OnPropertyChanged(nameof(ScenarioFanMode));
        OnPropertyChanged(nameof(ScenarioFanControlKind));
        OnPropertyChanged(nameof(IsScenarioFixedFan));
        OnPropertyChanged(nameof(IsScenarioCurveFan));
        OnPropertyChanged(nameof(ScenarioFanSpeedCpu));
        OnPropertyChanged(nameof(ScenarioFanSpeedGpu));
        OnPropertyChanged(nameof(ScenarioCurvePreset));
        OnPropertyChanged(nameof(ScenarioBacklightTimeout));
        OnPropertyChanged(nameof(ScenarioLightingEffect));
        OnPropertyChanged(nameof(ScenarioAppSyncEnabled));
        OnPropertyChanged(nameof(ScenarioZoneColor1));
        OnPropertyChanged(nameof(ScenarioZoneColor2));
        OnPropertyChanged(nameof(ScenarioZoneColor3));
        OnPropertyChanged(nameof(ScenarioZoneColor4));
        OnPropertyChanged(nameof(IsSelectedScenarioActive));
    }
}
