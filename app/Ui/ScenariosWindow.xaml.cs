using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nitrous.Helpers;
using Nitrous.Managers;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Nitrous.Ui;

/// <summary>Scenario Manager window. Edits via the shared DashboardViewModel; Tray applies.</summary>
public partial class ScenariosWindow : Window
{
    private readonly DashboardViewModel _viewModel;

    public ScenariosWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Hardware-limited devices: hide unsupported RGB editors.
        ApplyRgbVisibility();

        UpdateScenarioStatusText();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateZoneSwatches();

        UpdateChamferGeometry();
        this.Loaded += (_, _) => UpdateChamferGeometry();
        this.SizeChanged += (_, _) => UpdateChamferGeometry();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            || e.PropertyName.StartsWith("ScenarioZoneColor", StringComparison.Ordinal)
            || e.PropertyName == nameof(DashboardViewModel.SelectedScenario))
        {
            UpdateZoneSwatches();
        }
    }

    private KeyboardColor[] CurrentZoneColors() =>
    [
        KeyboardColor.FromHex(_viewModel.ScenarioZoneColor1),
        KeyboardColor.FromHex(_viewModel.ScenarioZoneColor2),
        KeyboardColor.FromHex(_viewModel.ScenarioZoneColor3),
        KeyboardColor.FromHex(_viewModel.ScenarioZoneColor4),
    ];

    private void UpdateZoneSwatches()
    {
        if (ScenarioZone1Btn == null) return;
        var colors = CurrentZoneColors();
        var buttons = new[] { ScenarioZone1Btn, ScenarioZone2Btn, ScenarioZone3Btn, ScenarioZone4Btn };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Background = new SolidColorBrush(Color.FromRgb(colors[i].R, colors[i].G, colors[i].B));
        }
        ScenarioZoneAllBtn.Background = BuildZonePreview(colors);
    }

    private Brush BuildZonePreview(KeyboardColor[] colors)
    {
        var first = colors[0];
        bool uniform = true;
        for (int i = 1; i < colors.Length; i++)
        {
            if (colors[i] != first) { uniform = false; break; }
        }

        if (uniform)
        {
            return new SolidColorBrush(Color.FromRgb(first.R, first.G, first.B));
        }

        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i < colors.Length; i++)
        {
            var col = Color.FromRgb(colors[i].R, colors[i].G, colors[i].B);
            gradient.GradientStops.Add(new GradientStop(col, (double)i / (colors.Length - 1)));
        }
        return gradient;
    }

    private void Zone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag || !int.TryParse(tag, out int zone)) return;

        // Edits the selected scenario's zone data only; nothing is written to
        // global KbZone settings or hardware until Tray applies the scenario.
        bool allZones = zone == 0;
        int index = zone - 1;
        if (!allZones && (index < 0 || index >= 4)) return;

        var colors = CurrentZoneColors();
        var current = colors[allZones ? 0 : index];

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var chosen = new KeyboardColor(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        string hex = chosen.ToHex();

        if (allZones)
        {
            _viewModel.ScenarioZoneColor1 = hex;
            _viewModel.ScenarioZoneColor2 = hex;
            _viewModel.ScenarioZoneColor3 = hex;
            _viewModel.ScenarioZoneColor4 = hex;
        }
        else
        {
            switch (index)
            {
                case 0: _viewModel.ScenarioZoneColor1 = hex; break;
                case 1: _viewModel.ScenarioZoneColor2 = hex; break;
                case 2: _viewModel.ScenarioZoneColor3 = hex; break;
                case 3: _viewModel.ScenarioZoneColor4 = hex; break;
            }
        }

        UpdateZoneSwatches();
    }

    public void UpdateChamferGeometry()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w <= 0 || double.IsNaN(w)) w = 880;
        if (h <= 0 || double.IsNaN(h)) h = 660;

        var shellGeom = ChamferGeometryHelper.CreateChamferPathGeometry(w, h, ChamferGeometryHelper.DefaultWindowChamfer);
        WindowRoot.Clip = shellGeom;
        WindowShellFrame.Data = shellGeom;

        if (w > 2 && h > 2)
        {
            var innerGeom = ChamferGeometryHelper.CreateChamferPathGeometry(w - 2, h - 2, ChamferGeometryHelper.DefaultWindowChamfer - 0.4);
            WindowContentGrid.Clip = innerGeom;
        }
    }

    private void ApplyRgbVisibility()
    {
        // Effects need the ENE HID controller; WMI-only keyboards expose static zone colors.
        LightingEffectPanel.Visibility =
            _viewModel.ScenarioRgbSupported && AcerKeyboard.SupportsEffects ? Visibility.Visible : Visibility.Collapsed;
        ZoneColorsPanel.Visibility = _viewModel.ScenarioRgbSupported ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void ScenarioEnabledCheck_Changed(object sender, RoutedEventArgs e) => UpdateScenarioStatusText();

    private void UpdateScenarioStatusText()
    {
        if (ScenarioStatusText == null) return;
        ScenarioStatusText.Text = _viewModel.ScenarioEnabled
            ? "Enabled. Tray applies scenarios and App Sync."
            : "Disabled. Turn on to let Tray apply scenarios and App Sync.";
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Applications (*.exe)|*.exe",
                Title = "Link an app to this scenario",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) == true)
            {
                _viewModel.AddLinkedApp(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Add linked app failed: {ex.Message}", "ScenarioWindow");
        }
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (ScenarioAppList.SelectedItem is ScenarioAppItem item)
        {
            _viewModel.RemoveLinkedApp(item.Path);
        }
    }
}
