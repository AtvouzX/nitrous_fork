using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Nitrous.Managers;
using Nitrous.Enums;

namespace Nitrous.Ui;

public partial class NitrousDashboard : Window
{
    private bool _isDialogOpen = false;

    public NitrousDashboard()
    {
        InitializeComponent();

        DataContext = new DashboardViewModel();

        DashVersionText.Text = $"Nitrous {UpdateManager.CurrentVersion}";
        SettingsVersionText.Text = $"Nitrous {UpdateManager.CurrentVersion}";
        GpuVersionText.Text = $"Nitrous {UpdateManager.CurrentVersion}";

        System.Threading.Tasks.Task.Run(() =>
        {
            string modelName = SystemInfoManager.GetSystemModel();
            Dispatcher.Invoke(() => SystemModelText.Text = $"{modelName}");
        });

        SystemEvents.PowerModeChanged += OnPowerStateChanged;
    }

    private void OnPowerStateChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
        {
            System.Threading.Tasks.Task.Delay(5500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => RefreshDashboardState());
            });
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => this.Close();

    private void NavDashBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Visible;
        GpuPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;

        var activeBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B388FF"));
        var inactiveBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888890"));

        NavDashIcon.Fill = activeBrush;
        NavDashText.Foreground = activeBrush;

        NavGpuIcon.Fill = inactiveBrush;
        NavGpuText.Foreground = inactiveBrush;

        NavSetIcon.Fill = inactiveBrush;
        NavSetText.Foreground = inactiveBrush;
    }

    private void NavGpuBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;

        var activeBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B388FF"));
        var inactiveBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888890"));

        NavDashIcon.Fill = inactiveBrush;
        NavDashText.Foreground = inactiveBrush;

        NavGpuIcon.Fill = activeBrush;
        NavGpuText.Foreground = activeBrush;

        NavSetIcon.Fill = inactiveBrush;
        NavSetText.Foreground = inactiveBrush;
    }

    private void NavSetBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;

        var activeBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B388FF"));
        var inactiveBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888890"));

        NavDashIcon.Fill = inactiveBrush;
        NavDashText.Foreground = inactiveBrush;

        NavGpuIcon.Fill = inactiveBrush;
        NavGpuText.Foreground = inactiveBrush;

        NavSetIcon.Fill = activeBrush;
        NavSetText.Foreground = activeBrush;
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (this.Visibility == Visibility.Visible) RefreshDashboardState();
    }

    public void RefreshDashboardState()
    {
        bool isOnline = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        var powerColor = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isOnline ? "#FF453A" : "#34C759"));
        string powerText = isOnline ? "AC POWER" : "BATTERY";

        var acGeom = Geometry.Parse("M16,7V3H14V7H10V3H8V7C8,10 9.79,11.4 11,11.83V16H13V11.83C14.21,11.4 16,10 16,7M10,18H14V22H10V18Z");
        var battGeom = Geometry.Parse("M16.67,4H15V2H9V4H7.33A1.33,1.33 0 0,0 6,5.33V20.67C6,21.4 6.6,22 7.33,22H16.67A1.33,1.33 0 0,0 18,20.67V5.33C18,4.6 17.4,4 16.67,4Z");

        DashPowerPillBorder.BorderBrush = powerColor;
        DashPowerPillIcon.Fill = powerColor;
        DashPowerPillText.Foreground = powerColor;
        DashPowerPillText.Text = powerText;
        DashPowerPillIcon.Data = isOnline ? acGeom : battGeom;

        // SettingsPowerPillBorder.BorderBrush = powerColor;
        // SettingsPowerPillIcon.Fill = powerColor;
        // SettingsPowerPillText.Foreground = powerColor;
        // SettingsPowerPillText.Text = powerText;
        // SettingsPowerPillIcon.Data = isOnline ? acGeom : battGeom;

        var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        var activeFan = Enum.TryParse(SettingsManager.Get("LastFanMode", "Auto"), out FanProfile f) ? f : FanProfile.Auto;
        var activeRefresh = (RefreshProfile)SettingsManager.Get("RefreshMode", (int)RefreshProfile.Auto);

        BtnPowerQuiet.IsChecked = activeMode == PowerProfile.Quiet;
        BtnPowerBal.IsChecked = activeMode == PowerProfile.Balanced;
        BtnPowerPerf.IsChecked = activeMode == PowerProfile.Performance;
        BtnPowerTurbo.IsChecked = activeMode == PowerProfile.Turbo;

        BtnFanAuto.IsChecked = activeFan == FanProfile.Auto;
        BtnFanMax.IsChecked = activeFan == FanProfile.Max;
        BtnFanCustom.IsChecked = activeFan == FanProfile.Medium;

        BtnRefreshAuto.IsChecked = activeRefresh == RefreshProfile.Auto;
        BtnRefresh60.IsChecked = activeRefresh == RefreshProfile.Hz60;
        BtnRefreshMax.IsChecked = activeRefresh == RefreshProfile.MaxHz;

        if (DataContext is DashboardViewModel vm)
        {
            vm.IsCustomFanEnabled = activeFan == FanProfile.Medium;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        this.Topmost = SettingsManager.Get("IsPinned", false);

        int top = SettingsManager.Get("WindowTop", -9999);
        int left = SettingsManager.Get("WindowLeft", -9999);
        if (top != -9999 && left != -9999)
        {
            this.Top = top;
            this.Left = left;
        }
        else
        {
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (this.Topmost || _isDialogOpen) return;

        this.WindowState = WindowState.Minimized;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SystemEvents.PowerModeChanged -= OnPowerStateChanged;
        SettingsManager.Save("WindowTop", (int)this.Top);
        SettingsManager.Save("WindowLeft", (int)this.Left);
        SettingsManager.Save("IsPinned", this.Topmost);
    }

    private FanCurveWindow? _activeCurveWindow;

    private void OpenCurveEditor_Click(object sender, RoutedEventArgs e)
    {
        // If the window is already open, just bring it to the front
        if (_activeCurveWindow != null && _activeCurveWindow.IsLoaded)
        {
            _activeCurveWindow.Activate();
            return;
        }

        _activeCurveWindow = new FanCurveWindow((DashboardViewModel)DataContext)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        double currentLeft = double.IsNaN(this.Left) ? (SystemParameters.WorkArea.Width / 2) - (this.Width / 2) : this.Left;
        double currentTop = double.IsNaN(this.Top) ? (SystemParameters.WorkArea.Height / 2) - (this.Height / 2) : this.Top;

        double targetLeft = currentLeft - _activeCurveWindow.Width - 10;

        if (targetLeft < 0)
        {
            targetLeft = currentLeft + this.Width + 10;
        }

        _activeCurveWindow.Left = targetLeft;
        _activeCurveWindow.Top = currentTop;

        // Attach an event to clear the flag when the window closes
        _activeCurveWindow.Closed += (s, args) => _isDialogOpen = false;

        _isDialogOpen = true;
        _activeCurveWindow.Show();
    }
}
