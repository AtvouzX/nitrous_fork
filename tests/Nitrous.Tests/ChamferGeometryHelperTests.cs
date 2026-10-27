using System.Windows.Media;
using Nitrous.Helpers;
using Xunit;
using Point = System.Windows.Point;

namespace Nitrous.Tests;

public class ChamferGeometryHelperTests
{
    [Fact]
    public void CreateChamferPoints_ReturnsExactSixPointsWithCorrectChamfer()
    {
        double width = 352.0;
        double height = 600.0;
        double chamfer = 10.0;

        var points = ChamferGeometryHelper.CreateChamferPoints(width, height, chamfer);

        Assert.Equal(6, points.Count);
        Assert.True(points.IsFrozen);

        // Point 0: Top-Left sharp corner (0, 0)
        Assert.Equal(new Point(0, 0), points[0]);

        // Point 1: Top-Right cut start (width - chamfer, 0)
        Assert.Equal(new Point(342.0, 0), points[1]);

        // Point 2: Top-Right cut end (width, chamfer)
        Assert.Equal(new Point(352.0, 10.0), points[2]);

        // Point 3: Bottom-Right sharp corner (width, height)
        Assert.Equal(new Point(352.0, 600.0), points[3]);

        // Point 4: Bottom-Left cut start (chamfer, height)
        Assert.Equal(new Point(10.0, 600.0), points[4]);

        // Point 5: Bottom-Left cut end (0, height - chamfer)
        Assert.Equal(new Point(0, 590.0), points[5]);
    }

    [Fact]
    public void CreateChamferPathGeometry_ProducesClosedAndFrozenPathFigure()
    {
        double width = 375.0;
        double height = 660.0;

        var geometry = ChamferGeometryHelper.CreateChamferPathGeometry(width, height, 10.0);

        Assert.NotNull(geometry);
        Assert.True(geometry.IsFrozen);
        Assert.Single(geometry.Figures);

        var figure = geometry.Figures[0];
        Assert.True(figure.IsClosed);
        Assert.True(figure.IsFilled);
        Assert.Equal(new Point(0, 0), figure.StartPoint);
        Assert.Equal(5, figure.Segments.Count);

        var seg0 = Assert.IsType<LineSegment>(figure.Segments[0]);
        Assert.Equal(new Point(365.0, 0), seg0.Point);

        var seg1 = Assert.IsType<LineSegment>(figure.Segments[1]);
        Assert.Equal(new Point(375.0, 10.0), seg1.Point);

        var seg2 = Assert.IsType<LineSegment>(figure.Segments[2]);
        Assert.Equal(new Point(375.0, 660.0), seg2.Point);

        var seg3 = Assert.IsType<LineSegment>(figure.Segments[3]);
        Assert.Equal(new Point(10.0, 660.0), seg3.Point);

        var seg4 = Assert.IsType<LineSegment>(figure.Segments[4]);
        Assert.Equal(new Point(0, 650.0), seg4.Point);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(350, 0)]
    [InlineData(-10, 500)]
    [InlineData(300, -5)]
    public void CreateChamfer_HandlesZeroOrNegativeDimensionsSafely(double width, double height)
    {
        var points = ChamferGeometryHelper.CreateChamferPoints(width, height);
        Assert.Empty(points);
        Assert.True(points.IsFrozen);

        var geometry = ChamferGeometryHelper.CreateChamferPathGeometry(width, height);
        Assert.Empty(geometry.Figures);
        Assert.True(geometry.IsFrozen);
    }

    [Fact]
    public void CreateChamfer_ClampsChamferSizeIfLargerThanHalfDimension()
    {
        // 100x60, chamfer 50 -> clamped to min(100, 60)/2 = 30
        var points = ChamferGeometryHelper.CreateChamferPoints(100.0, 60.0, 50.0);

        Assert.Equal(6, points.Count);
        Assert.Equal(new Point(0, 0), points[0]);
        Assert.Equal(new Point(70.0, 0), points[1]);
        Assert.Equal(new Point(100.0, 30.0), points[2]);
        Assert.Equal(new Point(100.0, 60.0), points[3]);
        Assert.Equal(new Point(30.0, 60.0), points[4]);
        Assert.Equal(new Point(0, 30.0), points[5]);
    }
}
