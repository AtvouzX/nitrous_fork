using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nitrous.Helpers;

namespace Nitrous.Ui.Controls;

/// <summary>
/// A reusable card container that renders sharp chamfered diagonal cuts
/// matching the industrial Nitrous Ignite forge aesthetic.
/// </summary>
public class ChamferCard : ContentControl
{
    public static readonly DependencyProperty ChamferSizeProperty =
        DependencyProperty.Register(
            nameof(ChamferSize),
            typeof(double),
            typeof(ChamferCard),
            new FrameworkPropertyMetadata(
                ChamferGeometryHelper.DefaultCardChamfer,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnGeometryChanged));

    public double ChamferSize
    {
        get => (double)GetValue(ChamferSizeProperty);
        set => SetValue(ChamferSizeProperty, value);
    }

    private Path? _framePath;
    private Border? _contentHost;

    public ChamferCard()
    {
        Template = CreateDefaultTemplate();
        SizeChanged += (_, _) => UpdateCardGeometry();
        Loaded += (_, _) => UpdateCardGeometry();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _framePath = GetTemplateChild("PART_Frame") as Path;
        _contentHost = GetTemplateChild("PART_ContentHost") as Border;
        UpdateCardGeometry();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == BorderBrushProperty || e.Property == BackgroundProperty || e.Property == BorderThicknessProperty)
        {
            UpdateCardGeometry();
        }
    }

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChamferCard card)
        {
            card.UpdateCardGeometry();
        }
    }

    public void UpdateCardGeometry()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;

        if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0)
        {
            return;
        }

        double c = ChamferSize > 0 ? ChamferSize : ChamferGeometryHelper.DefaultCardChamfer;
        var geom = ChamferGeometryHelper.CreateChamferPathGeometry(w, h, c);

        Clip = geom;

        if (_framePath != null)
        {
            double strokeThickness = BorderThickness.Left > 0 ? BorderThickness.Left : (BorderBrush != null ? 1.0 : 0.0);
            double halfStroke = strokeThickness > 0 ? strokeThickness / 2.0 : 0.0;
            var frameGeom = ChamferGeometryHelper.CreateChamferPathGeometry(w, h, c, halfStroke);

            _framePath.Data = frameGeom;
            _framePath.Stroke = BorderBrush;
            _framePath.Fill = Background;
            _framePath.StrokeThickness = strokeThickness;
        }

        if (_contentHost != null && w > 2 && h > 2)
        {
            var innerGeom = ChamferGeometryHelper.CreateChamferPathGeometry(w - 2, h - 2, Math.Max(0, c - 0.4));
            _contentHost.Clip = innerGeom;
        }
    }

    private static ControlTemplate CreateDefaultTemplate()
    {
        var template = new ControlTemplate(typeof(ChamferCard));
        var grid = new FrameworkElementFactory(typeof(Grid));

        var path = new FrameworkElementFactory(typeof(Path));
        path.Name = "PART_Frame";
        path.SetValue(Shape.StrokeProperty, new TemplateBindingExtension(BorderBrushProperty));
        path.SetValue(Shape.FillProperty, new TemplateBindingExtension(BackgroundProperty));
        path.SetValue(Shape.StrokeThicknessProperty, 1.0);
        path.SetValue(SnapsToDevicePixelsProperty, true);
        grid.AppendChild(path);

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "PART_ContentHost";
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetValue(SnapsToDevicePixelsProperty, true);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(presenter);
        grid.AppendChild(border);

        template.VisualTree = grid;
        template.Seal();
        return template;
    }
}
