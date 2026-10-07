using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Enums;
using System.ComponentModel;

using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using Panel = System.Windows.Controls.Panel;

namespace Nitrous.Ui;

public partial class FanCurveWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private List<Point> _cpuPoints = new();
    private List<Point> _gpuPoints = new();
    private List<Point> ActivePoints => TabCpu.IsChecked == true ? _cpuPoints : _gpuPoints;

    private readonly List<UIElement> _pointHandles = new();
    private int _draggingIndex = -1;

    private const double MinTemp = 30.0;
    private const double MaxTemp = 100.0;
    private const double MinSpeed = 0.0;
    private const double MaxSpeed = 100.0;

    public FanCurveWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Listen for Power Profile changes from the Dashboard
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Load initial curves based on the currently active profile
        LoadCurvesForProfile(_viewModel.ActivePowerProfile);

        this.Loaded += (s, e) => RedrawGraph();
        GraphCanvas.SizeChanged += (s, e) => RedrawGraph();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // If the user clicks a power profile on the Dashboard, swap the curves in real-time
        if (e.PropertyName == nameof(DashboardViewModel.ActivePowerProfile))
        {
            LoadCurvesForProfile(_viewModel.ActivePowerProfile);
        }
        // If the user switches Fan Mode away from Custom, close the editor automatically
        else if (e.PropertyName == nameof(DashboardViewModel.IsCustomFanEnabled))
        {
            if (!_viewModel.IsCustomFanEnabled)
            {
                this.Close();
            }
        }
    }

    private void LoadCurvesForProfile(PowerProfile profile)
    {
        string suffix = profile.ToString();

        // Load saved registry curve, OR fallback to our new distinct defaults if not customized yet
        _cpuPoints = FanCurveHelper.LoadCurveFromRegistry($"CpuCurve_{suffix}", FanCurveHelper.GetDefaultCpuCurve(profile));
        _gpuPoints = FanCurveHelper.LoadCurveFromRegistry($"GpuCurve_{suffix}", FanCurveHelper.GetDefaultGpuCurve(profile));

        if (this.IsLoaded) RedrawGraph();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (GraphCanvas != null) RedrawGraph();
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TabCpu.IsChecked == true)
            _cpuPoints = new List<Point>(FanCurveHelper.GetDefaultCpuCurve(_viewModel.ActivePowerProfile));
        else
            _gpuPoints = new List<Point>(FanCurveHelper.GetDefaultGpuCurve(_viewModel.ActivePowerProfile));

        RedrawGraph();
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        // 1. Respect the Curve Override state from the ViewModel
        if (_viewModel.IsCurveModeEnabled)
            _viewModel.IsCustomFanEnabled = true;

        // 2. Save curves to registry for the active power profile
        string suffix = _viewModel.ActivePowerProfile.ToString();
        FanCurveHelper.SaveCurveToRegistry($"CpuCurve_{suffix}", _cpuPoints);
        FanCurveHelper.SaveCurveToRegistry($"GpuCurve_{suffix}", _gpuPoints);

        // 3. Persist settings and bump version so background engine immediately reloads
        SettingsManager.Save("IsCurveModeEnabled", _viewModel.IsCurveModeEnabled ? 1 : 0);
        SettingsManager.Save("LastFanMode", "Medium");
        bool isOnline = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                        System.Windows.Forms.PowerLineStatus.Online;
        SettingsManager.Save(isOnline ? "LastAcFanMode" : "LastDcFanMode", "Medium");
        SettingsManager.Save("FanCurveVersion", DateTime.UtcNow.Ticks);

        // 4. Immediately evaluate current telemetry and apply fans to hardware EC
        try
        {
            var telemetry = AcerWmiManager.GetSystemTelemetry();
            int currentCpuTemp = telemetry.CpuTemp > 0 ? telemetry.CpuTemp : 50;
            int currentGpuTemp = telemetry.GpuTemp > 0 ? telemetry.GpuTemp : currentCpuTemp;

            int cpuSpd = Math.Clamp(FanCurveHelper.InterpolateSpeed(_cpuPoints, currentCpuTemp), 0, 100);
            int gpuSpd = Math.Clamp(FanCurveHelper.InterpolateSpeed(_gpuPoints, currentGpuTemp), 0, 100);

            await AcerWmiManager.SetCustomFansAsync(cpuSpd, gpuSpd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to immediately apply fan curve: {ex.Message}");
        }

        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void RedrawGraph()
    {
        // Prevent execution if window is rendering early or lists aren't loaded yet
        if (GraphCanvas.ActualWidth == 0 || GraphCanvas.ActualHeight == 0) return;
        if (_cpuPoints == null || _gpuPoints == null) return;

        // Sort lists in place to prevent horizontal dragging glitches
        if (TabCpu.IsChecked == true)
            _cpuPoints = _cpuPoints.OrderBy(p => p.X).ToList();
        else
            _gpuPoints = _gpuPoints.OrderBy(p => p.X).ToList();

        foreach (var handle in _pointHandles) GraphCanvas.Children.Remove(handle);
        _pointHandles.Clear();

        var points = ActivePoints;
        var pointCollection = new PointCollection();

        // 1. Extend flat line to the left edge if the first point > 30C
        double firstPx = TempToX(points.First().X);
        double firstPy = SpeedToY(points.First().Y);
        if (firstPx > 0)
        {
            pointCollection.Add(new Point(0, firstPy));
        }

        // 2. Draw actual curve points & styled SVG handles
        for (int i = 0; i < points.Count; i++)
        {
            double px = TempToX(points[i].X);
            double py = SpeedToY(points[i].Y);
            var canvasPt = new Point(px, py);

            pointCollection.Add(canvasPt);

            // Create the outer stroke ring
            var outerRing = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0c1018")),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a855f7")),
                StrokeThickness = 2.5,
                Cursor = Cursors.Hand,
                Tag = i
            };

            // Create the inner solid dot
            var innerDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a855f7")),
                IsHitTestVisible = false // Let the outer ring handle clicks
            };

            Canvas.SetLeft(outerRing, px - 9);
            Canvas.SetTop(outerRing, py - 9);

            Canvas.SetLeft(innerDot, px - 3);
            Canvas.SetTop(innerDot, py - 3);

            outerRing.MouseLeftButtonDown += Handle_MouseDown;

            _pointHandles.Add(outerRing);
            _pointHandles.Add(innerDot);

            GraphCanvas.Children.Add(outerRing);
            GraphCanvas.Children.Add(innerDot);
        }

        // 3. Extend flat line to the right edge if last point < 100C
        double lastPx = TempToX(points.Last().X);
        double lastPy = SpeedToY(points.Last().Y);
        if (lastPx < GraphCanvas.ActualWidth)
        {
            pointCollection.Add(new Point(GraphCanvas.ActualWidth, lastPy));
        }

        CurveLine.Points = pointCollection;

        // Ensure tooltip stays on top of drawn lines and nodes
        Panel.SetZIndex(NodeTooltip, 999);
    }

    private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse handle && handle.Tag is int index)
        {
            _draggingIndex = index;
            NodeTooltip.Visibility = Visibility.Visible;
            Mouse.Capture(GraphCanvas);
            UpdateTooltipPosition();
            e.Handled = true;
        }
    }

    private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingIndex == -1) return;

        var pos = e.GetPosition(GraphCanvas);
        double newTemp = XToTemp(pos.X);
        double newSpeed = YToSpeed(pos.Y);

        newSpeed = Math.Max(MinSpeed, Math.Min(MaxSpeed, newSpeed));

        // 1. Apply Snap to Grid logic (Rounds to nearest 5)
        if (TogSnapToGrid.IsChecked == true)
        {
            newTemp = Math.Round(newTemp / 5.0) * 5.0;
            newSpeed = Math.Round(newSpeed / 5.0) * 5.0;
        }

        // 2. Constrain X (Temperature) dynamically based on neighbors
        // Ensure nodes can never overlap. If snapping is on, enforce a 5-degree gap, otherwise 2-degree.
        double minGap = TogSnapToGrid.IsChecked == true ? 5.0 : 2.0;

        double prevTemp = _draggingIndex > 0 ? ActivePoints[_draggingIndex - 1].X + minGap : MinTemp;
        double nextTemp = _draggingIndex < ActivePoints.Count - 1 ? ActivePoints[_draggingIndex + 1].X - minGap : MaxTemp;

        newTemp = Math.Max(prevTemp, Math.Min(nextTemp, newTemp));

        ActivePoints[_draggingIndex] = new Point(newTemp, newSpeed);

        RedrawGraph();
        UpdateTooltipPosition();
    }

    private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingIndex = -1;
        NodeTooltip.Visibility = Visibility.Hidden;
        Mouse.Capture(null);
    }

    private void GraphCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (Mouse.Captured != GraphCanvas)
        {
            _draggingIndex = -1;
            NodeTooltip.Visibility = Visibility.Hidden;
        }
    }

    private void UpdateTooltipPosition()
    {
        if (_draggingIndex == -1) return;

        var pt = ActivePoints[_draggingIndex];
        double px = TempToX(pt.X);
        double py = SpeedToY(pt.Y);

        TooltipText.Text = $"{(int)pt.X}°C · {(int)pt.Y}%";

        // Center the tooltip horizontally above the node
        Canvas.SetLeft(NodeTooltip, px - (NodeTooltip.ActualWidth / 2));
        Canvas.SetTop(NodeTooltip, py - 45); // Offset vertically
    }

    // --- Coordinate Math Helpers ---
    private double TempToX(double temp) => ((temp - MinTemp) / (MaxTemp - MinTemp)) * GraphCanvas.ActualWidth;
    private double XToTemp(double x) => (x / GraphCanvas.ActualWidth) * (MaxTemp - MinTemp) + MinTemp;

    private double SpeedToY(double speed) => GraphCanvas.ActualHeight - (((speed - MinSpeed) / (MaxSpeed - MinSpeed)) * GraphCanvas.ActualHeight);
    private double YToSpeed(double y) => ((GraphCanvas.ActualHeight - y) / GraphCanvas.ActualHeight) * (MaxSpeed - MinSpeed) + MinSpeed;
}
