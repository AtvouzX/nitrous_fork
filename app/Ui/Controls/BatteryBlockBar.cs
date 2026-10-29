using System;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using DrawingContext = System.Windows.Media.DrawingContext;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;

namespace Nitrous.Ui.Controls;

/// <summary>
/// A 20-segment battery charge block meter matching the Nitrous Ignite industrial aesthetic.
/// Each segment represents 5% of battery charge. Highlights the 80% ceiling when charge limit is active.
/// </summary>
public class BatteryBlockBar : FrameworkElement
{
    public static readonly DependencyProperty ChargePercentProperty =
        DependencyProperty.Register(
            nameof(ChargePercent),
            typeof(double),
            typeof(BatteryBlockBar),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ChargePercent
    {
        get => (double)GetValue(ChargePercentProperty);
        set => SetValue(ChargePercentProperty, value);
    }

    public static readonly DependencyProperty IsLimit80ActiveProperty =
        DependencyProperty.Register(
            nameof(IsLimit80Active),
            typeof(bool),
            typeof(BatteryBlockBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsLimit80Active
    {
        get => (bool)GetValue(IsLimit80ActiveProperty);
        set => SetValue(IsLimit80ActiveProperty, value);
    }

    private static readonly SolidColorBrush ActiveSteelBrush = CreateFrozen(0xED, 0xED, 0xEF);   // Steel100
    private static readonly SolidColorBrush ActiveEmberBrush = CreateFrozen(0xD9, 0x4E, 0x1F);   // Ember500
    private static readonly SolidColorBrush InactiveBrush = CreateFrozen(0x18, 0x18, 0x1C);      // Steel800
    private static readonly SolidColorBrush LimitMarkerFill = CreateFrozen(0x36, 0x13, 0x09);    // Ember900
    private static readonly SolidColorBrush LimitMarkerBorder = CreateFrozen(0xE8, 0x5D, 0x2A);  // Ember400
    private static readonly Pen LimitMarkerPen = new Pen(LimitMarkerBorder, 1.0);

    static BatteryBlockBar()
    {
        LimitMarkerPen.Freeze();
    }

    private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public BatteryBlockBar()
    {
        SnapsToDevicePixels = true;
        Height = 8.0;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double h = double.IsNaN(Height) || Height <= 0 ? 8.0 : Height;
        double w = double.IsNaN(Width) || Width <= 0 ? (double.IsInfinity(availableSize.Width) ? 200.0 : availableSize.Width) : Width;
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double totalWidth = ActualWidth;
        double h = ActualHeight > 0 ? ActualHeight : 8.0;

        if (totalWidth <= 0 || h <= 0) return;

        const int segmentCount = 20;
        const double gap = 2.0;
        double totalGaps = (segmentCount - 1) * gap;
        double segWidth = Math.Max(1.0, (totalWidth - totalGaps) / segmentCount);

        double pct = Math.Clamp(ChargePercent, 0.0, 100.0);
        // Each segment represents 5% (20 segments = 100%)
        int activeSegments = (int)Math.Round(pct / 5.0);

        for (int i = 0; i < segmentCount; i++)
        {
            double x = i * (segWidth + gap);
            var rect = new Rect(x, 0, segWidth, h);

            bool isActive = i < activeSegments;
            bool isAt80Mark = i == 15; // 16th segment = 80% mark (16 * 5% = 80%)

            Brush fill;
            Pen? pen = null;

            if (isActive)
            {
                if (isAt80Mark && IsLimit80Active)
                {
                    fill = ActiveEmberBrush;
                    pen = LimitMarkerPen;
                }
                else if (i >= 14 && IsLimit80Active)
                {
                    // Near 80% ceiling indicator matching ignite.html
                    fill = ActiveEmberBrush;
                }
                else
                {
                    fill = ActiveSteelBrush;
                }
            }
            else
            {
                if (isAt80Mark && IsLimit80Active)
                {
                    fill = LimitMarkerFill;
                    pen = LimitMarkerPen;
                }
                else
                {
                    fill = InactiveBrush;
                }
            }

            dc.DrawRectangle(fill, pen, rect);
        }
    }
}
