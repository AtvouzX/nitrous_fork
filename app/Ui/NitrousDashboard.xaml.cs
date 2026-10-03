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
    private long _lastActivationTick = Environment.TickCount64;

    public NitrousDashboard()
    {
        InitializeComponent();

        DataContext = new DashboardViewModel();

        DashVersionText.Text = GpuVersionText.Text = KeyboardVersionText.Text = SettingsVersionText.Text = $"Nitrous {UpdateManager.CurrentVersion}";

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

    private static readonly SolidColorBrush ActiveBrush = CreateFrozenBrush("#B388FF");
    private static readonly SolidColorBrush InactiveBrush = CreateFrozenBrush("#888890");
    private static readonly SolidColorBrush AcColorBrush = CreateFrozenBrush("#FF453A");
    private static readonly SolidColorBrush BattColorBrush = CreateFrozenBrush("#34C759");

    private static readonly Geometry AcGeom = Geometry.Parse("M16,7V3H14V7H10V3H8V7C8,10 9.79,11.4 11,11.83V16H13V11.83C14.21,11.4 16,10 16,7M10,18H14V22H10V18Z");
    private static readonly Geometry BattGeom = Geometry.Parse("M16.67,4H15V2H9V4H7.33A1.33,1.33 0 0,0 6,5.33V20.67C6,21.4 6.6,22 7.33,22H16.67A1.33,1.33 0 0,0 18,20.67V5.33C18,4.6 17.4,4 16.67,4Z");

    static NitrousDashboard()
    {
        AcGeom.Freeze();
        BattGeom.Freeze();
    }

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
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

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (this.Visibility == Visibility.Visible) RefreshDashboardState();
    }

    public void RefreshDashboardState()
    {
        bool isOnline = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        var powerColor = isOnline ? AcColorBrush : BattColorBrush;
        string powerText = isOnline ? "AC POWER" : "BATTERY";

        DashPowerPillBorder.BorderBrush = powerColor;
        DashPowerPillIcon.Fill = powerColor;
        DashPowerPillText.Foreground = powerColor;
        DashPowerPillText.Text = powerText;
        DashPowerPillIcon.Data = isOnline ? AcGeom : BattGeom;

        var activeMode = AcerWmiManager.GetActivePowerMode() ?? (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
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
            // vm.IsCustomFanEnabled = activeFan == FanProfile.Medium;
            vm.ActivePowerProfile = activeMode;
        }

        // Refresh Hotkey labels
        if (TxtHkCycle != null)
        {
            TxtHkCycle.Text = FormatHotkeyLabel(SettingsManager.Get("Hotkey_CyclePower", ""));
            TxtHkDash.Text = FormatHotkeyLabel(SettingsManager.Get("Hotkey_Dashboard", ""));
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _lastActivationTick = Environment.TickCount64;
        this.Topmost = SettingsManager.Get("IsPinned", false);

        // Dock to Windows Quick Settings position (bottom-right above taskbar)
        this.Left = SystemParameters.WorkArea.Right - this.Width - 12;
        this.Top = SystemParameters.WorkArea.Bottom - this.Height - 12;
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public void RestoreAndActivate()
    {
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
            const int SW_RESTORE = 9;
            ShowWindow(helper.Handle, SW_RESTORE);
            SetForegroundWindow(helper.Handle);
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
        SystemEvents.PowerModeChanged -= OnPowerStateChanged;
        SettingsManager.Save("IsPinned", this.Topmost);
        (DataContext as IDisposable)?.Dispose();
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

    private void Hotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true; // Prevent standard typing

        if (sender is not System.Windows.Controls.TextBox txt || txt.Tag == null) return;

        // Ignore modifier keys pressed on their own
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.System)
            return;

        string settingKey = (string)txt.Tag;

        // ESC clears the hotkey
        if (e.Key == Key.Escape)
        {
            txt.Text = "None";
            SettingsManager.Save(settingKey, "");
            Keyboard.ClearFocus();
            return;
        }

        // Extract actual key (handle System keys like Alt+Key)
        Key key = (e.Key == Key.System ? e.SystemKey : e.Key);
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        // Calculate modifiers
        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;

        // Require at least one modifier to prevent binding simple letters like "W"
        if (modifiers == 0) return;

        // Save as "Modifiers|VirtualKey"
        string savedValue = $"{modifiers}|{vk}";
        SettingsManager.Save(settingKey, savedValue);

        txt.Text = FormatHotkeyLabel(savedValue);
        Keyboard.ClearFocus();
    }

    private string FormatHotkeyLabel(string savedValue)
    {
        if (string.IsNullOrEmpty(savedValue)) return "None";
        try
        {
            var parts = savedValue.Split('|');
            uint mods = uint.Parse(parts[0]);
            uint vk = uint.Parse(parts[1]);

            string label = "";
            if ((mods & 0x0002) != 0) label += "Ctrl + ";
            if ((mods & 0x0004) != 0) label += "Shift + ";
            if ((mods & 0x0001) != 0) label += "Alt + ";

            label += KeyInterop.KeyFromVirtualKey((int)vk).ToString();
            return label;
        }
        catch { return "None"; }
    }
}
