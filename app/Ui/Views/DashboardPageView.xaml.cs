using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nitrous.Managers;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Nitrous.Ui.Views;

public partial class DashboardPageView : UserControl
{
    private bool _lcdOverdriveLoading = true;
    private bool _lcdOverdriveBusy;
    private bool _lcdOverdriveAvailable;

    public DashboardPageView()
    {
        InitializeComponent();
        InitializeLcdOverdrive();
    }

    public void InitializeLcdOverdrive()
    {
        var state = AcerWmiManager.GetLcdOverdrive();
        if (state is { Supported: true })
            SettingsManager.Save("LcdOverdriveRefused", 0);

        var refused = SettingsManager.Get("LcdOverdriveRefused", 0) == 1;
        _lcdOverdriveAvailable = state is { Supported: true } && !refused;

        if (_lcdOverdriveAvailable && state is { On: var on })
            TogLcdOverdrive.IsChecked = on;

        SetLcdOverdriveVisible(_lcdOverdriveAvailable);
        _lcdOverdriveLoading = false;
    }

    private const double TelemetryHeightNormal = 172;
    private const double TelemetryHeightCompact = 136; // frees the overdrive row height (~36px)

    private void SetLcdOverdriveVisible(bool visible)
    {
        LcdOverdriveRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // The page has no scroll region and only ~9px spare at window height;
        // the overdrive row would push Refresh/Fans past the bottom edge.
        if (TelemetryGrid != null)
        {
            TelemetryGrid.Height = visible ? TelemetryHeightCompact : TelemetryHeightNormal;
        }
    }

    private void SetLcdOverdriveToggle(bool on)
    {
        _lcdOverdriveLoading = true;
        TogLcdOverdrive.IsChecked = on;
        _lcdOverdriveLoading = false;
    }

    private async void LcdOverdrive_Toggled(object sender, RoutedEventArgs e)
    {
        if (_lcdOverdriveLoading || _lcdOverdriveBusy || !_lcdOverdriveAvailable) return;

        var wanted = TogLcdOverdrive.IsChecked == true;
        _lcdOverdriveBusy = true;

        try
        {
            if (!AcerWmiManager.SetLcdOverdrive(wanted))
            {
                SetLcdOverdriveToggle(!wanted);
                return;
            }

            var applied = await ReadLcdOverdriveAfterWriteAsync(wanted);
            if (applied is { On: var on } && on == wanted) return;

            SettingsManager.Save("LcdOverdriveRefused", 1);
            _lcdOverdriveAvailable = false;
            SetLcdOverdriveToggle(false);
            SetLcdOverdriveVisible(false);
        }
        finally
        {
            _lcdOverdriveBusy = false;
        }
    }

    private static async Task<(bool Supported, bool On)?> ReadLcdOverdriveAfterWriteAsync(bool wanted)
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(200);
            var state = AcerWmiManager.GetLcdOverdrive();
            if (state is { On: var on } && on == wanted) return state;
        }
        return AcerWmiManager.GetLcdOverdrive();
    }

    private void CurveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.IsCurveModeEnabled = !vm.IsCurveModeEnabled;
        }
    }

    private void UnifyFans_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.IsUnifiedFans = !vm.IsUnifiedFans;
        }
    }

    private void OpenCurveEditor_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is NitrousDashboard dash)
        {
            dash.OpenCurveEditor();
        }
    }

    private void OpenScenarioManager_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is NitrousDashboard dash)
        {
            dash.OpenScenarioManager();
        }
    }

    private void Graph_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && sender is FrameworkElement fe)
        {
            var pos = e.GetPosition(fe);
            if (fe.Name == "CpuGraphGrid") vm.UpdateCpuHover(pos.X, fe.ActualWidth);
            else if (fe.Name == "GpuGraphGrid") vm.UpdateGpuHover(pos.X, fe.ActualWidth);
        }
    }

    private void Graph_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && sender is FrameworkElement fe)
        {
            if (fe.Name == "CpuGraphGrid") vm.CpuHoverVisibility = Visibility.Collapsed;
            else if (fe.Name == "GpuGraphGrid") vm.GpuHoverVisibility = Visibility.Collapsed;
        }
    }

    private void Graph_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && sender is FrameworkElement fe && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            if (fe.Name == "CpuGraphGrid") vm.UpdateCpuGraphDimensions(e.NewSize.Width, e.NewSize.Height);
            else if (fe.Name == "GpuGraphGrid") vm.UpdateGpuGraphDimensions(e.NewSize.Width, e.NewSize.Height);
        }
    }
}
