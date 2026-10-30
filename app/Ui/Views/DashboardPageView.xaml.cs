using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Nitrous.Ui.Views;

public partial class DashboardPageView : UserControl
{
    public DashboardPageView()
    {
        InitializeComponent();
    }

    private void CurveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.IsCurveModeEnabled = !vm.IsCurveModeEnabled;
        }
    }

    private void OpenCurveEditor_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is NitrousDashboard dash)
        {
            dash.OpenCurveEditor();
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
