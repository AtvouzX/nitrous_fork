using System.Drawing;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class OsdLayoutHelperTests
{
    [Fact]
    public void Compute_At96Dpi_ReturnsDesignBaseline()
    {
        var layout = OsdLayoutHelper.Compute(1.0);

        Assert.Equal(new Size(280, 70), layout.FormSize);
        Assert.Equal(10, layout.Chamfer);
        Assert.Equal(8, layout.AccentBarWidth);
        Assert.Equal(new Rectangle(25, 17, 36, 36), layout.IconBounds);
        Assert.Equal(new Rectangle(65, 0, 215, 70), layout.TextRect);
        Assert.Equal(160, layout.BottomMargin);
    }

    [Fact]
    public void Compute_At144Dpi_ScalesEveryPixelMetric()
    {
        var layout = OsdLayoutHelper.Compute(1.5);

        Assert.Equal(new Size(420, 105), layout.FormSize);
        Assert.Equal(15, layout.Chamfer);
        Assert.Equal(12, layout.AccentBarWidth);
        Assert.Equal(new Rectangle(38, 26, 54, 54), layout.IconBounds);
        Assert.Equal(new Rectangle(98, 0, 322, 105), layout.TextRect);
        Assert.Equal(240, layout.BottomMargin);
    }

    [Fact]
    public void Compute_At192Dpi_DoublesEveryPixelMetric()
    {
        var layout = OsdLayoutHelper.Compute(2.0);

        Assert.Equal(new Size(560, 140), layout.FormSize);
        Assert.Equal(20, layout.Chamfer);
        Assert.Equal(16, layout.AccentBarWidth);
        Assert.Equal(new Rectangle(50, 34, 72, 72), layout.IconBounds);
        Assert.Equal(new Rectangle(130, 0, 430, 140), layout.TextRect);
        Assert.Equal(320, layout.BottomMargin);
    }

    [Fact]
    public void Compute_ClampsScaleToSafeRange()
    {
        var shrunk = OsdLayoutHelper.Compute(0.5);
        Assert.Equal(new Size(280, 70), shrunk.FormSize);

        var oversized = OsdLayoutHelper.Compute(8.0);
        Assert.Equal(new Size(1120, 280), oversized.FormSize);
    }

    [Fact]
    public void Compute_RoundsHalfPixelsAwayFromZero()
    {
        // 1.25 scale: 36 * 1.25 = 45 exact, 17 * 1.25 = 21.25 -> 21, 25 * 1.25 = 31.25 -> 31
        var layout = OsdLayoutHelper.Compute(1.25);

        Assert.Equal(new Size(350, 88), layout.FormSize);
        Assert.Equal(new Rectangle(31, 21, 45, 45), layout.IconBounds);
        Assert.Equal(new Rectangle(81, 0, 269, 88), layout.TextRect);
    }
}
