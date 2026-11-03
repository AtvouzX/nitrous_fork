using System;
using System.Windows;
using System.Windows.Input;
using Nitrous.Helpers;
using Nitrous.Managers;

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
        if (!_viewModel.ScenarioRgbSupported)
        {
            LightingEffectPanel.Visibility = Visibility.Collapsed;
            ZoneColorsPanel.Visibility = Visibility.Collapsed;
        }

        UpdateScenarioStatusText();

        UpdateChamferGeometry();
        this.Loaded += (_, _) => UpdateChamferGeometry();
        this.SizeChanged += (_, _) => UpdateChamferGeometry();
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
