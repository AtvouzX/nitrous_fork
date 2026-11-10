using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Nitrous.Managers;
using Nitrous.Enums;
using Nitrous.Interop;
using Nitrous.Helpers;

namespace Nitrous.Ui;

public partial class NitrousDashboard : Window
{
    private bool _isDialogOpen = false;
    private long _lastActivationTick = Environment.TickCount64;

    public System.Windows.Controls.RadioButton BtnPowerEco => DashPage.BtnPowerEco;
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

        var dashboardViewModel = new DashboardViewModel();
        DataContext = dashboardViewModel;
        dashboardViewModel.PropertyChanged += OnDashboardViewModelPropertyChanged;

        UpdateChamferGeometry();
        NavDashBtn.Background = NavActiveBgBrush;

        LogManager.Info("NitrousDashboard initialized.", "UI");

        if (IsTestHost())
        {
            SystemModelText.Text = "ACER NITRO TEST UNIT";
        }
        else
        {
            SystemEvents.PowerModeChanged += OnPowerStateChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            System.Threading.Tasks.Task.Run(() =>
            {
                string modelName = SystemInfoManager.GetSystemModel();
                Dispatcher.Invoke(() => SystemModelText.Text = $"{modelName.ToUpperInvariant()}");
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

    private void Window_Loaded(object sender, RoutedEventArgs e) => UpdateChamferGeometry();

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChamferGeometry();

    public void UpdateChamferGeometry()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w <= 0 || double.IsNaN(w)) w = 375;
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

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() => DockToTaskbarCorner(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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

    private static readonly SolidColorBrush ActiveBrush = CreateFrozenBrush("#D94E1F");
    private static readonly SolidColorBrush InactiveBrush = CreateFrozenBrush("#8A8A96");
    private static readonly SolidColorBrush NavActiveBgBrush = CreateFrozenBrush("#121215");
    private static readonly SolidColorBrush NavInactiveBgBrush = CreateFrozenBrush("#00000000");

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
        NavDashBtn.Background = NavActiveBgBrush;

        NavGpuIcon.Fill = InactiveBrush;
        NavGpuText.Foreground = InactiveBrush;
        NavGpuBtn.Background = NavInactiveBgBrush;

        NavKeyboardIcon.Fill = InactiveBrush;
        NavKeyboardText.Foreground = InactiveBrush;
        NavKeyboardBtn.Background = NavInactiveBgBrush;

        NavSetIcon.Fill = InactiveBrush;
        NavSetText.Foreground = InactiveBrush;
        NavSetBtn.Background = NavInactiveBgBrush;

    }

    private void NavGpuBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        KeyboardPage.Visibility = Visibility.Collapsed;

        NavDashIcon.Fill = InactiveBrush;
        NavDashText.Foreground = InactiveBrush;
        NavDashBtn.Background = NavInactiveBgBrush;

        NavGpuIcon.Fill = ActiveBrush;
        NavGpuText.Foreground = ActiveBrush;
        NavGpuBtn.Background = NavActiveBgBrush;

        NavKeyboardIcon.Fill = InactiveBrush;
        NavKeyboardText.Foreground = InactiveBrush;
        NavKeyboardBtn.Background = NavInactiveBgBrush;

        NavSetIcon.Fill = InactiveBrush;
        NavSetText.Foreground = InactiveBrush;
        NavSetBtn.Background = NavInactiveBgBrush;

    }

    private void NavKeyboardBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Collapsed;
        KeyboardPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;

        NavDashIcon.Fill = InactiveBrush;
        NavDashText.Foreground = InactiveBrush;
        NavDashBtn.Background = NavInactiveBgBrush;

        NavGpuIcon.Fill = InactiveBrush;
        NavGpuText.Foreground = InactiveBrush;
        NavGpuBtn.Background = NavInactiveBgBrush;

        NavKeyboardIcon.Fill = ActiveBrush;
        NavKeyboardText.Foreground = ActiveBrush;
        NavKeyboardBtn.Background = NavActiveBgBrush;

        NavSetIcon.Fill = InactiveBrush;
        NavSetText.Foreground = InactiveBrush;
        NavSetBtn.Background = NavInactiveBgBrush;

    }

    private void NavSetBtn_Click(object sender, RoutedEventArgs e)
    {
        DashPage.Visibility = Visibility.Collapsed;
        GpuPage.Visibility = Visibility.Collapsed;
        KeyboardPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;

        NavDashIcon.Fill = InactiveBrush;
        NavDashText.Foreground = InactiveBrush;
        NavDashBtn.Background = NavInactiveBgBrush;

        NavGpuIcon.Fill = InactiveBrush;
        NavGpuText.Foreground = InactiveBrush;
        NavGpuBtn.Background = NavInactiveBgBrush;

        NavKeyboardIcon.Fill = InactiveBrush;
        NavKeyboardText.Foreground = InactiveBrush;
        NavKeyboardBtn.Background = NavInactiveBgBrush;


        NavSetIcon.Fill = ActiveBrush;
        NavSetText.Foreground = ActiveBrush;
        NavSetBtn.Background = NavActiveBgBrush;
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
        if (this.Visibility == Visibility.Visible)
        {
            CheckForPowerModeDrift();
            RefreshDashboardState();
        }
    }

    private void CheckForPowerModeDrift()
    {
        try
        {
            var detected = AcerWmiManager.GetActivePowerMode();
            var saved = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

            if (HardwareSafetyPolicy.HasDrift(detected, saved))
            {
                var resolved = HardwareSafetyPolicy.ResolveDrift(detected, saved);
                LogManager.Info($"Hardware power mode drift detected on window activation: {saved} -> {resolved}", "UI");
                bool isOnline = AcerWmiManager.GetPowerState().OnAc;
                SettingsManager.Save("LastPowerMode", (int)resolved);
                SettingsManager.Save(isOnline ? "LastAcPowerMode" : "LastDcPowerMode", (int)resolved);

                if (DataContext is DashboardViewModel vm)
                {
                    vm.ActivePowerProfile = resolved;
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Drift check on window activation failed: {ex.Message}", "UI");
        }
    }

    public void RefreshDashboardState()
    {
        var activeMode = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
        var activeFan = Enum.TryParse(SettingsManager.Get("LastFanMode", "Auto"), out FanProfile f)
            ? f
            : FanProfile.Auto;
        var activeRefresh = (RefreshProfile)SettingsManager.Get("RefreshMode", (int)RefreshProfile.Auto);

        BtnPowerEco.IsChecked = activeMode == PowerProfile.Eco;
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
            vm.SyncActiveFanMode(activeFan);
            vm.ActiveRefreshProfile = activeRefresh;
            vm.UpdateBatteryBadge();
            vm.RefreshWhisperState();
        }

        // Refresh Hotkey labels in keyboard view
        KeyboardPage?.RefreshHotkeys();
        KeyboardPage?.InitializeKeyboardLighting();
        KeyboardPage?.RefreshBacklightUi();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _lastActivationTick = Environment.TickCount64;
        this.Topmost = SettingsManager.Get("IsPinned", false);

        DockToTaskbarCorner();
    }

    /// <summary>
    /// Docks bottom-right above the taskbar on the monitor nearest the cursor,
    /// following taskbar moves and monitor changes. Test hook bypasses Screen lookup.
    /// </summary>
    internal void DockToTaskbarCorner(
        System.Drawing.Rectangle? workAreaOverride = null,
        System.Drawing.Point? cursorOverride = null)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        var cursor = cursorOverride ?? System.Windows.Forms.Cursor.Position;
        var area = workAreaOverride ?? NearestWorkingArea(screens, cursor);

        // WinForms pixels -> WPF device-independent units.
        var source = PresentationSource.FromVisual(this);
        double scaleX = 1.0, scaleY = 1.0;
        if (source?.CompositionTarget != null)
        {
            scaleX = source.CompositionTarget.TransformFromDevice.M11;
            scaleY = source.CompositionTarget.TransformFromDevice.M22;
        }

        double dashW = double.IsNaN(Width) || Width <= 0 ? 375 : Width;
        double dashH = double.IsNaN(Height) || Height <= 0 ? 580 : Height;
        var (left, top) = Helpers.WindowPlacementHelper.ComputeDashboardDock(
            area.Left * scaleX, area.Top * scaleY, area.Right * scaleX, area.Bottom * scaleY,
            dashW, dashH);

        this.Left = left;
        this.Top = top;
    }

    private static System.Drawing.Rectangle NearestWorkingArea(
        System.Windows.Forms.Screen[] screens, System.Drawing.Point cursor)
    {
        System.Windows.Forms.Screen? best = null;
        double bestDist = double.MaxValue;
        foreach (var s in screens)
        {
            if (s.WorkingArea.Contains(cursor)) return s.WorkingArea;
            double dx = cursor.X < s.WorkingArea.Left ? s.WorkingArea.Left - cursor.X
                : cursor.X > s.WorkingArea.Right ? cursor.X - s.WorkingArea.Right : 0;
            double dy = cursor.Y < s.WorkingArea.Top ? s.WorkingArea.Top - cursor.Y
                : cursor.Y > s.WorkingArea.Bottom ? cursor.Y - s.WorkingArea.Bottom : 0;
            double dist = dx * dx + dy * dy;
            if (dist < bestDist) { bestDist = dist; best = s; }
        }
        return best?.WorkingArea ?? screens[0].WorkingArea;
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
        HandleWindowStateChanged(this.WindowState);
    }

    /// <summary>Test seam: state reaction without needing a shown HWND.</summary>
    internal void HandleWindowStateChanged(WindowState state)
    {
        if (state == WindowState.Minimized)
        {
            (DataContext as DashboardViewModel)?.PausePolling();
            Nitrous.Helpers.MemoryHelper.TrimWorkingSet();
        }
        else if (state == WindowState.Normal)
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

        DockToTaskbarCorner();
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

        CheckForPowerModeDrift();
        RefreshDashboardState();
    }

    /// <summary>
    /// Keeps the fan rail in step with the view model. Switching into a mode whose
    /// firmware fan table owns the fans drops the rail back to Auto, so the UI never
    /// claims manual control the EC refuses.
    /// </summary>
    private void OnDashboardViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardViewModel.IsAutoFanMode) ||
            DataContext is not DashboardViewModel vm)
        {
            return;
        }

        BtnFanAuto.IsChecked = vm.IsAutoFanMode;
        BtnFanMax.IsChecked = vm.IsMaxFanMode;
        BtnFanCustom.IsChecked = vm.IsCustomFanMode;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        LogManager.Info("Dashboard window closed; disposing resources.", "UI");
        SystemEvents.PowerModeChanged -= OnPowerStateChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SettingsManager.Save("IsPinned", this.Topmost);
        if (DataContext is DashboardViewModel viewModel) viewModel.PropertyChanged -= OnDashboardViewModelPropertyChanged;
        (DataContext as IDisposable)?.Dispose();
    }

    private ScenariosWindow? _activeScenarioWindow;

    internal void OpenScenarioManager()
    {
        if (_activeScenarioWindow != null && _activeScenarioWindow.IsLoaded)
        {
            _activeScenarioWindow.Activate();
            return;
        }

        _activeScenarioWindow = new ScenariosWindow((DashboardViewModel)DataContext)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        PositionCompanionWindow(_activeScenarioWindow);
        _activeScenarioWindow.Closed += (_, _) => { _activeScenarioWindow = null; _isDialogOpen = false; };
        _isDialogOpen = true;
        _activeScenarioWindow.Show();
    }

    /// <summary>
    /// Docks a companion window to the left of the dashboard using the same
    /// placement as the Fan Curve editor, clamped inside the nearest monitor.
    /// </summary>
    internal void PositionCompanionWindow(Window companion)
    {
        companion.WindowStartupLocation = WindowStartupLocation.Manual;

        double currentLeft = double.IsNaN(this.Left)
            ? (SystemParameters.WorkArea.Width / 2) - (this.Width / 2)
            : this.Left;
        double currentTop = double.IsNaN(this.Top)
            ? (SystemParameters.WorkArea.Height / 2) - (this.Height / 2)
            : this.Top;

        // Clamp to the monitor nearest the dashboard (not just primary).
        // Screens report device pixels; dashboard Left/Top are DIPs — convert first.
        var source = PresentationSource.FromVisual(this);
        var toDevice = source?.CompositionTarget?.TransformToDevice;
        var centerDip = new System.Windows.Point(currentLeft + this.Width / 2, currentTop + this.Height / 2);
        var centerDev = toDevice.HasValue ? toDevice.Value.Transform(centerDip) : centerDip;
        var cursorDev = new System.Drawing.Point((int)centerDev.X, (int)centerDev.Y);
        var area = NearestWorkingArea(System.Windows.Forms.Screen.AllScreens, cursorDev);
        double scaleX = 1.0, scaleY = 1.0;
        if (source?.CompositionTarget != null)
        {
            scaleX = source.CompositionTarget.TransformFromDevice.M11;
            scaleY = source.CompositionTarget.TransformFromDevice.M22;
        }
        var (targetLeft, targetTop) = Helpers.WindowPlacementHelper.ComputeCurveEditorPosition(
            currentLeft, currentTop, this.Width, this.Height,
            companion.Width, companion.Height,
            area.Left * scaleX, area.Top * scaleY, area.Right * scaleX, area.Bottom * scaleY);

        companion.Left = targetLeft;
        companion.Top = targetTop;
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

        PositionCompanionWindow(_activeCurveWindow);

        // Attach an event to clear the flag when the window closes
        _activeCurveWindow.Closed += (s, args) => _isDialogOpen = false;

        _isDialogOpen = true;
        _activeCurveWindow.Show();
    }
}



