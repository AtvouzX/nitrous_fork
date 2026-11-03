using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Nitrous.Helpers;
using Point = System.Windows.Point;
using Xunit;

namespace Nitrous.Tests;

public class ChamferGeometryConverterTests
{
    [Fact]
    public void Convert_WithWidthHeight_ReturnsSixPointChamferGeometry()
    {
        var converter = new ChamferGeometryConverter();
        var result = Assert.IsType<PathGeometry>(
            converter.Convert(new object[] { 200.0, 100.0 }, typeof(object), "6;0.5", CultureInfo.InvariantCulture));

        var figure = Assert.Single(result.Figures);
        Assert.Equal(5, figure.Segments.Count);
        Assert.Equal(new Point(0.5, 0.5), figure.StartPoint);
    }

    [Fact]
    public void Convert_WithDefaultParameter_UsesCardChamfer()
    {
        var converter = new ChamferGeometryConverter();
        var result = Assert.IsType<PathGeometry>(
            converter.Convert(new object[] { 200.0, 100.0 }, typeof(object), null, CultureInfo.InvariantCulture));

        var figure = result.Figures[0];
        // Default chamfer 6.0: first segment ends 6px before the top-right edge.
        Assert.Equal(200.0 - 6.0, figure.Segments[0] is LineSegment line ? line.Point.X : double.NaN);
    }

    [Fact]
    public void Convert_WithInvalidValues_ReturnsEmptyGeometry()
    {
        var converter = new ChamferGeometryConverter();
        Assert.Equal(Geometry.Empty, converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture));
        Assert.Equal(Geometry.Empty, converter.Convert(new object[] { "not-a-double", 100.0 }, typeof(object), null, CultureInfo.InvariantCulture));
        Assert.Equal(Geometry.Empty, converter.Convert(new object[] { 0.0, 0.0 }, typeof(object), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_ReturnsEmptyArray()
    {
        var converter = new ChamferGeometryConverter();
        Assert.Empty(converter.ConvertBack(Geometry.Empty, new[] { typeof(double) }, null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_GeometryIsFrozen_ForCrossThreadSafety()
    {
        var converter = new ChamferGeometryConverter();
        var result = (PathGeometry)converter.Convert(new object[] { 100.0, 50.0 }, typeof(object), "6", CultureInfo.InvariantCulture);
        Assert.True(result.IsFrozen);
    }
}
