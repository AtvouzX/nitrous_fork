using System;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Nitrous.Helpers;

/// <summary>
/// Pure mathematical and geometric helper to compute chamfered polygon shapes
/// for Nitrous windows, cards, and buttons, matching the industrial forge aesthetic.
/// </summary>
public static class ChamferGeometryHelper
{
    public const double DefaultWindowChamfer = 10.0;
    public const double DefaultCardChamfer = 6.0;
    public const double DefaultButtonChamfer = 4.0;

    /// <summary>
    /// Computes the 6-point chamfer polygon PathGeometry for a rectangle of given width and height.
    /// Top-Left: (0, 0) - sharp right angle with 0 radius.
    /// Top-Right cut: (width - chamfer, 0) to (width, chamfer).
    /// Bottom-Right: (width, height) - sharp right angle with 0 radius.
    /// Bottom-Left cut: (chamfer, height) to (0, height - chamfer).
    /// </summary>
    public static PathGeometry CreateChamferPathGeometry(double width, double height, double chamferSize = DefaultWindowChamfer)
    {
        if (width <= 0 || height <= 0)
        {
            var empty = new PathGeometry();
            empty.Freeze();
            return empty;
        }

        double c = Math.Clamp(chamferSize, 0, Math.Min(width, height) / 2.0);

        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new LineSegment(new Point(width - c, 0), true));
        figure.Segments.Add(new LineSegment(new Point(width, c), true));
        figure.Segments.Add(new LineSegment(new Point(width, height), true));
        figure.Segments.Add(new LineSegment(new Point(c, height), true));
        figure.Segments.Add(new LineSegment(new Point(0, height - c), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Computes the 6-point chamfer polygon PointCollection for a rectangle of given width and height.
    /// </summary>
    public static PointCollection CreateChamferPoints(double width, double height, double chamferSize = DefaultWindowChamfer)
    {
        var points = new PointCollection();
        if (width <= 0 || height <= 0)
        {
            points.Freeze();
            return points;
        }

        double c = Math.Clamp(chamferSize, 0, Math.Min(width, height) / 2.0);

        points.Add(new Point(0, 0));
        points.Add(new Point(width - c, 0));
        points.Add(new Point(width, c));
        points.Add(new Point(width, height));
        points.Add(new Point(c, height));
        points.Add(new Point(0, height - c));

        points.Freeze();
        return points;
    }
}
