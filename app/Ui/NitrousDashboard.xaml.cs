using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Nitrous.Managers;
using Nitrous.Enums;
using Nitrous.Interop;

namespace Nitrous.Ui;

public partial class NitrousDashboard : Window
{
    private bool _isDialogOpen = false;
    private long _lastActivationTick = Environment.TickCount64;

    public System.Windows.Controls.RadioButton BtnPowerQuiet => DashPage.BtnPowerQuiet;
    public System.Windows.Controls.RadioButton BtnPowerBal => DashPage.BtnPowerBal;
    public System.Windows.Controls.RadioButton BtnPowerPerf => DashPage.BtnPowerPerf;
    public System.Windows.Controls.RadioButton BtnPowerTurbo => DashPage.BtnPowerTurbo;

    public System.Windows.Controls.RadioButton BtnFanAuto => DashPage.BtnFanAuto;
    public System.Windows.Controls.RadioButton BtnFanMax => DashPage.BtnFanMax;
    public System.Windows.Controls.RadioButton BtnFanCustom => DashPage.BtnFanCustom;

    public System.Windows.Controls.RadioButton BtnRefreshAuto => DashPage.BtnRefreshAuto;
    public System.Windows.Controls.RadioButton BtnRefresh60 => DashPage.BtnRefresh60;
    public System.Windows.Controls.RadioButton BtnRefreshMax => DashPage.BtnRefreshMax;

    public System.Windows.Controls.CheckBox TogDebugging => SettingsPage.TogDebugging;
    public System.Windows.Controls.Button BtnOpenLogsFolder => SettingsPage.BtnOpenLogsFolder;
    public System.Windows.Controls.Button BtnCopyDiagnostics => SettingsPage.BtnCopyDiagnostics;
    public System.Windows.Controls.CheckBox TogAutoApplyClocks => SettingsPage.TogAutoApplyClocks;

    public NitrousDashboard()
    {
        InitializeComponent();

        DataContext = new DashboardViewModel();

        LogManager.Info("NitrousDashboard initialized.", "UI");

        if (IsTestHost())
        {
            SystemModelText.Text = "Acer Nitro Test Unit";
        }
        else
        {
            SystemEvents.PowerModeChanged += OnPowerStateChanged;

            System.Threading.Tasks.Task.Run(() =>
            {
                string modelName = SystemInfoManager.GetSystemModel();
                Dispatcher.Invoke(() => SystemModelText.Text = $"{modelName}");
            });

            this.Loaded += (s, e) =>
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(600);
                    Nitrous.Helpers.MemoryHelper.TrimWorkingSet();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
        }
    }

    private void OnPowerStateChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
        {
            // Update UI quickly for the Power Pill (AC/Battery state)
            System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => RefreshDashboardState());
            });
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private static readonly SolidColorBrush ActiveBrush = CreateFrozenBrush("#FF4500");
    private static readonly SolidColorBrush InactiveBrush = CreateFrozenBrush("#A1A1AA");

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => this.Close();

    private void NavDashBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Visible;
        GpuPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        KeyboardPage.Visibility = Visibility.Collapsed;

        NavDashIcon.Fill = ActiveBrush;
        NavDashText.Foreground = ActiveBrush;

        NavGpuIcon.Fill = InactiveBrush;
        NavGpuText.Foreground = InactiveBrush;

        NavKeyboardIcon.Fill = InactiveBrush;
        NavKeyboardText.Foreground = InactiveBrush;

        NavSetIcon.Fill = InactiveBrush;
        NavSetText.Foreground = InactiveBrush;
    }

    private void NavGpuBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        KeyboardPage.Visibility = Visibility.Collapsed;

        NavDashIcon.Fill = InactiveBrush;
        NavDashText.Foreground = InactiveBrush;

        NavGpuIcon.Fill = ActiveBrush;
        NavGpuText.Foreground = ActiveBrush;

        NavKeyboardIcon.Fill = InactiveBrush;
        NavKeyboardText.Foreground = InactiveBrush;

        NavSetIcon.Fill = InactiveBrush;
        NavSetText.Foreground = InactiveBrush;
    }

    private void NavKeyboardBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Collapsed;
        KeyboardPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;

        NavDashIcon.Fill = InactiveBrush;
        NavDashText.Foreground = InactiveBrush;

        NavGpuIcon.Fill = InactiveBrush;
        NavGpuText.Foreground = InactiveBrush;

        NavKeyboardIcon.Fill = ActiveBrush;
        NavKeyboardText.Foreground = ActiveBrush;

        NavSetIcon.Fill = InactiveBrush;
        NavSetText.Foreground = InactiveBrush;
    }

    private void NavSetBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Collapsed;
        KeyboardPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;

        NavDashIcon.Fill = InactiveBrush;
        NavDashText.Foreground = InactiveBrush;

        NavGpuIcon.Fill = InactiveBrush;
        NavGpuText.Foreground = InactiveBrush;

        NavKeyboardIcon.Fill = InactiveBrush;
        NavKeyboardText.Foreground = InactiveBrush;

        NavSetIcon.Fill = ActiveBrush;
        NavSetText.Foreground = ActiveBrush;
    }

    internal void NavigateToDashboard() => NavDashBtn_Click(this, new RoutedEventArgs());
    internal void NavigateToGpu() => NavGpuBtn_Click(this, new RoutedEventArgs());
    internal void NavigateToKeyboard() => NavKeyboardBtn_Click(this, new RoutedEventArgs());
    internal void NavigateToSettings() => NavSetBtn_Click(this, new RoutedEventArgs());

    private static bool IsTestHost()
    {
        string procName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        return procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
               procName.Contains("vstest", StringComparison.OrdinalIgnoreCase);
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (this.Visibility == Visibility.Visible) RefreshDashboardState();
    }

    public void RefreshDashboardState()
    {
        var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        var activeFan = Enum.TryParse(SettingsManager.Get("LastFanMode", "Auto"), out FanProfile f)
            ? f
            : FanProfile.Auto;
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
            vm.ActivePowerProfile = activeMode;
            vm.UpdateBatteryBadge();
        }

        // Refresh Hotkey labels in keyboard view
        KeyboardPage?.RefreshHotkeys();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _lastActivationTick = Environment.TickCount64;
        this.Topmost = SettingsManager.Get("IsPinned", false);

        // Dock to Windows Quick Settings position (bottom-right above taskbar)
        this.Left = SystemParameters.WorkArea.Right - this.Width - 12;
        this.Top = SystemParameters.WorkArea.Bottom - this.Height - 50;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (this.Topmost || _isDialogOpen) return;

        // Grace period: ignore transient deactivation during process startup or companion IFEO execution
        if (Environment.TickCount64 - _lastActivationTick < 1500) return;

        this.WindowState = WindowState.Minimized;
        Nitrous.Helpers.MemoryHelper.TrimWorkingSet();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (this.WindowState == WindowState.Minimized)
        {
            (DataContext as DashboardViewModel)?.PausePolling();
            Nitrous.Helpers.MemoryHelper.TrimWorkingSet();
        }
        else if (this.WindowState == WindowState.Normal)
        {
            (DataContext as DashboardViewModel)?.ResumePolling();
        }
    }

    public void RestoreAndActivate()
    {
        LogManager.Info("Dashboard RestoreAndActivate requested.", "UI");
        _lastActivationTick = Environment.TickCount64;

        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }

        this.Show();
        this.Activate();
        this.Focus();

        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            User32.ShowWindow(helper.Handle, User32.SW_RESTORE);
            User32.SetForegroundWindow(helper.Handle);
        }

        if (_activeCurveWindow != null && _activeCurveWindow.IsLoaded)
        {
            _activeCurveWindow.Activate();
        }

        RefreshDashboardState();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        LogManager.Info("Dashboard window closed; disposing resources.", "UI");
        SystemEvents.PowerModeChanged -= OnPowerStateChanged;
        SettingsManager.Save("IsPinned", this.Topmost);
        (DataContext as IDisposable)?.Dispose();
    }

    private FanCurveWindow? _activeCurveWindow;

    internal void OpenCurveEditor()
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

        double currentLeft = double.IsNaN(this.Left)
            ? (SystemParameters.WorkArea.Width / 2) - (this.Width / 2)
            : this.Left;
        double currentTop = double.IsNaN(this.Top)
            ? (SystemParameters.WorkArea.Height / 2) - (this.Height / 2)
            : this.Top;

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

