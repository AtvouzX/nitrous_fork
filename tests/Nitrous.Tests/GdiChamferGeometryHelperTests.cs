using System.Drawing;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class GdiChamferGeometryHelperTests
{
    [Fact]
    public void CreateChamferPoints_ReturnsExactSixPointsWithCorrectChamfer()
    {
        var points = GdiChamferGeometryHelper.CreateChamferPoints(new RectangleF(0, 0, 280, 70), 10f);

        Assert.Equal(6, points.Length);
        Assert.Equal(new PointF(0, 0), points[0]);        // Top-Left sharp
        Assert.Equal(new PointF(270, 0), points[1]);      // Top-Right cut start
        Assert.Equal(new PointF(280, 10), points[2]);     // Top-Right cut end
        Assert.Equal(new PointF(280, 70), points[3]);     // Bottom-Right sharp
        Assert.Equal(new PointF(10, 70), points[4]);      // Bottom-Left cut start
        Assert.Equal(new PointF(0, 60), points[5]);       // Bottom-Left cut end
    }

    [Fact]
    public void CreateChamferPoints_HonoursRectOrigin()
    {
        var points = GdiChamferGeometryHelper.CreateChamferPoints(new RectangleF(0.5f, 0.5f, 279, 69), 10f);

        Assert.Equal(new PointF(0.5f, 0.5f), points[0]);
        Assert.Equal(new PointF(279.5f, 10.5f), points[2]);
        Assert.Equal(new PointF(10.5f, 69.5f), points[4]);
    }

    [Fact]
    public void CreateChamferPoints_ClampsChamferToHalfOfSmallestSide()
    {
        var points = GdiChamferGeometryHelper.CreateChamferPoints(new RectangleF(0, 0, 40, 40), 100f);

        Assert.Equal(new PointF(20, 0), points[1]);
        Assert.Equal(new PointF(40, 20), points[2]);
        Assert.Equal(new PointF(0, 20), points[5]);
    }

    [Fact]
    public void CreateChamferPoints_ReturnsEmptyForDegenerateRect()
    {
        Assert.Empty(GdiChamferGeometryHelper.CreateChamferPoints(new RectangleF(0, 0, 0, 70), 10f));
        Assert.Empty(GdiChamferGeometryHelper.CreateChamferPoints(new RectangleF(0, 0, 280, -1), 10f));
    }
}
