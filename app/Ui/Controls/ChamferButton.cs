using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nitrous.Helpers;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Nitrous.Ui.Controls;

public enum ChamferButtonVariant
{
    Primary,
    Secondary,
    Accent = Primary,
    Ghost = Secondary
}

/// <summary>
/// A high-performance WPF button control featuring sharp chamfered diagonal cuts
/// matching the Nitrous Ignite industrial forge aesthetic.
/// Provides high-contrast Primary (Ember) and Secondary (Steel) visual tiers.
/// </summary>
public class ChamferButton : Button
{
    public static readonly DependencyProperty ChamferSizeProperty =
        DependencyProperty.Register(
            nameof(ChamferSize),
            typeof(double),
            typeof(ChamferButton),
            new FrameworkPropertyMetadata(
                ChamferGeometryHelper.DefaultButtonChamfer,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnGeometryChanged));

    public double ChamferSize
    {
        get => (double)GetValue(ChamferSizeProperty);
        set => SetValue(ChamferSizeProperty, value);
    }

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(
            nameof(Variant),
            typeof(ChamferButtonVariant),
            typeof(ChamferButton),
            new FrameworkPropertyMetadata(
                ChamferButtonVariant.Secondary,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnVariantChanged));

    public ChamferButtonVariant Variant
    {
        get => (ChamferButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private Path? _framePath;
    private Border? _contentHost;

    // High-contrast static cached frozen brushes
    // Primary (Ember Forge Accent - for Restore, Report, Apply)
    private static readonly SolidColorBrush PrimaryNormalFill = CreateFrozenBrush(0xD9, 0x4E, 0x1F);   // Ember500
    private static readonly SolidColorBrush PrimaryNormalStroke = CreateFrozenBrush(0xE8, 0x5D, 0x2A); // Ember400
    private static readonly SolidColorBrush PrimaryHoverFill = CreateFrozenBrush(0xE8, 0x5D, 0x2A);    // Ember400
    private static readonly SolidColorBrush PrimaryHoverStroke = CreateFrozenBrush(0xFF, 0xFF, 0xFF);  // White highlight
    private static readonly SolidColorBrush PrimaryPressedFill = CreateFrozenBrush(0xC2, 0x41, 0x16);  // Ember600
    private static readonly SolidColorBrush PrimaryPressedStroke = CreateFrozenBrush(0x9A, 0x34, 0x12);// Ember700
    private static readonly SolidColorBrush PrimaryForeground = CreateFrozenBrush(0x08, 0x08, 0x0A);   // Deep Jet Black (6.5:1 AAA contrast)

    // Secondary (Gunmetal Industrial Steel - for Copy, Logs, Reset)
    private static readonly SolidColorBrush SecondaryNormalFill = CreateFrozenBrush(0x12, 0x12, 0x15);   // Steel850
    private static readonly SolidColorBrush SecondaryNormalStroke = CreateFrozenBrush(0x36, 0x36, 0x3E); // Steel600
    private static readonly SolidColorBrush SecondaryHoverFill = CreateFrozenBrush(0x18, 0x18, 0x1C);    // Steel800
    private static readonly SolidColorBrush SecondaryHoverStroke = CreateFrozenBrush(0x8A, 0x8A, 0x96);  // Steel400 (high-contrast outline)
    private static readonly SolidColorBrush SecondaryPressedFill = CreateFrozenBrush(0x08, 0x08, 0x0A);  // Steel950
    private static readonly SolidColorBrush SecondaryPressedStroke = CreateFrozenBrush(0x36, 0x36, 0x3E);// Steel600
    private static readonly SolidColorBrush SecondaryForeground = CreateFrozenBrush(0xED, 0xED, 0xEF);   // Steel100 Crisp Silver-White (13.5:1 contrast)
    private static readonly SolidColorBrush SecondaryHoverForeground = CreateFrozenBrush(0xFF, 0xFF, 0xFF); // Pure White

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public ChamferButton()
    {
        Template = CreateDefaultTemplate();
        Cursor = System.Windows.Input.Cursors.Hand;
        FontFamily = new FontFamily("JetBrains Mono, Consolas, Segoe UI Mono, monospace");
        FontWeight = FontWeights.Bold;
        FontSize = 10.5;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(1);
        SizeChanged += (_, _) => UpdateButtonGeometry();
        Loaded += (_, _) => UpdateButtonGeometry();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _framePath = GetTemplateChild("PART_Frame") as Path;
        _contentHost = GetTemplateChild("PART_ContentHost") as Border;
        UpdateButtonGeometry();
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateVisualBrushes();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateVisualBrushes();
    }

    protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        UpdateVisualBrushes();
    }

    protected override void OnPreviewMouseUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        UpdateVisualBrushes();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == BorderBrushProperty || e.Property == BackgroundProperty)
        {
            UpdateVisualBrushes();
            UpdateButtonGeometry();
        }
    }

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChamferButton btn)
        {
            btn.UpdateButtonGeometry();
        }
    }

    private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChamferButton btn)
        {
            btn.UpdateVisualBrushes();
        }
    }

    public void UpdateVisualBrushes()
    {
        if (_framePath == null) return;

        if (!IsEnabled)
        {
            Opacity = 0.35;
            Cursor = System.Windows.Input.Cursors.Arrow;
            return;
        }

        Opacity = 1.0;
        Cursor = System.Windows.Input.Cursors.Hand;

        bool hasCustomBg = ReadLocalValue(BackgroundProperty) != DependencyProperty.UnsetValue && Background != Brushes.Transparent && Background != null;
        bool hasCustomBorder = ReadLocalValue(BorderBrushProperty) != DependencyProperty.UnsetValue && BorderBrush != null;

        if (Variant == ChamferButtonVariant.Primary)
        {
            if (IsPressed)
            {
                _framePath.Fill = hasCustomBg ? Background : PrimaryPressedFill;
                _framePath.Stroke = hasCustomBorder ? BorderBrush : PrimaryPressedStroke;
                Foreground = PrimaryForeground;
            }
            else if (IsMouseOver)
            {
                _framePath.Fill = hasCustomBg ? Background : PrimaryHoverFill;
                _framePath.Stroke = PrimaryHoverStroke;
                Foreground = PrimaryForeground;
            }
            else
            {
                _framePath.Fill = hasCustomBg ? Background : PrimaryNormalFill;
                _framePath.Stroke = hasCustomBorder ? BorderBrush : PrimaryNormalStroke;
                Foreground = PrimaryForeground;
            }
        }
        else // Secondary
        {
            if (IsPressed)
            {
                _framePath.Fill = hasCustomBg ? Background : SecondaryPressedFill;
                _framePath.Stroke = hasCustomBorder ? BorderBrush : SecondaryPressedStroke;
                Foreground = SecondaryHoverForeground;
            }
            else if (IsMouseOver)
            {
                _framePath.Fill = hasCustomBg ? Background : SecondaryHoverFill;
                _framePath.Stroke = SecondaryHoverStroke;
                Foreground = SecondaryHoverForeground;
            }
            else
            {
                _framePath.Fill = hasCustomBg ? Background : SecondaryNormalFill;
                _framePath.Stroke = hasCustomBorder ? BorderBrush : SecondaryNormalStroke;
                Foreground = SecondaryForeground;
            }
        }
    }

    public void UpdateButtonGeometry()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;

        if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0)
        {
            return;
        }

        double c = ChamferSize > 0 ? ChamferSize : ChamferGeometryHelper.DefaultButtonChamfer;
        var geom = ChamferGeometryHelper.CreateChamferPathGeometry(w, h, c);
        Clip = geom;

        if (_framePath != null)
        {
            double strokeThickness = BorderThickness.Left > 0 ? BorderThickness.Left : 1.0;
            double halfStroke = strokeThickness > 0 ? strokeThickness / 2.0 : 0.0;
            var frameGeom = ChamferGeometryHelper.CreateChamferPathGeometry(w, h, c, halfStroke);

            _framePath.Data = frameGeom;
            _framePath.StrokeThickness = strokeThickness;
        }

        if (_contentHost != null && w > 2 && h > 2)
        {
            var innerGeom = ChamferGeometryHelper.CreateChamferPathGeometry(w - 2, h - 2, Math.Max(0, c - 0.4));
            _contentHost.Clip = innerGeom;
        }

        UpdateVisualBrushes();
    }

    private static ControlTemplate CreateDefaultTemplate()
    {
        var template = new ControlTemplate(typeof(ChamferButton));
        var grid = new FrameworkElementFactory(typeof(Grid));

        var path = new FrameworkElementFactory(typeof(Path));
        path.Name = "PART_Frame";
        path.SetValue(Shape.StrokeThicknessProperty, 1.0);
        path.SetValue(SnapsToDevicePixelsProperty, true);
        grid.AppendChild(path);

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "PART_ContentHost";
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        border.SetValue(SnapsToDevicePixelsProperty, true);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(PaddingProperty));
        border.AppendChild(presenter);
        grid.AppendChild(border);

        template.VisualTree = grid;
        template.Seal();
        return template;
    }
}
