using System.Drawing;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class OsdLayoutHelperTests
{
    [Fact]
    public void Compute_At96Dpi_ReturnsReferenceBaseline()
    {
        var layout = OsdLayoutHelper.Compute(1.0);

        Assert.Equal(new Size(250, 85), layout.FormSize);
        Assert.Equal(19, layout.Chamfer);
        Assert.Equal(new Rectangle(24, 26, 32, 32), layout.IconBounds);
        Assert.Equal(new Rectangle(72, 22, 2, 40), layout.DividerRect);
        Assert.Equal(new Rectangle(90, 0, 132, 85), layout.TextRect);
        Assert.Equal(160, layout.BottomMargin);
        Assert.Equal(70, layout.CornerAccentRun);
        Assert.Equal(35, layout.CornerAccentDrop);
    }

    [Fact]
    public void Compute_NarrowContent_ClampsToMinWidth()
    {
        var layout = OsdLayoutHelper.Compute(1.5, 150);

        Assert.Equal(new Size(375, 128), layout.FormSize);
        Assert.Equal(29, layout.Chamfer);
        Assert.Equal(new Rectangle(36, 40, 48, 48), layout.IconBounds);
        Assert.Equal(new Rectangle(108, 34, 3, 60), layout.DividerRect);
        Assert.Equal(new Rectangle(135, 0, 198, 128), layout.TextRect);
        Assert.Equal(240, layout.BottomMargin);
    }

    [Fact]
    public void Compute_At192Dpi_DoublesEveryPixelMetric()
    {
        var layout = OsdLayoutHelper.Compute(2.0);

        Assert.Equal(new Size(500, 170), layout.FormSize);
        Assert.Equal(38, layout.Chamfer);
        Assert.Equal(new Rectangle(48, 53, 64, 64), layout.IconBounds);
        Assert.Equal(new Rectangle(144, 45, 4, 80), layout.DividerRect);
        Assert.Equal(new Rectangle(180, 0, 264, 170), layout.TextRect);
        Assert.Equal(320, layout.BottomMargin);
    }

    [Fact]
    public void Compute_WideContent_GrowsBetweenMinAndMaxWidth()
    {
        var layout = OsdLayoutHelper.Compute(1.0, 350);

        Assert.Equal(new Size(468, 85), layout.FormSize);
        Assert.Equal(new Rectangle(90, 0, 350, 85), layout.TextRect);
    }

    [Fact]
    public void Compute_OversizeContent_ClampsToMaxWidth()
    {
        var layout = OsdLayoutHelper.Compute(1.0, 1000);

        Assert.Equal(new Size(600, 85), layout.FormSize);
        Assert.Equal(new Rectangle(90, 0, 482, 85), layout.TextRect);
    }

    [Fact]
    public void Compute_ClampsScaleToSafeRange()
    {
        var shrunk = OsdLayoutHelper.Compute(0.5);
        Assert.Equal(new Size(250, 85), shrunk.FormSize);

        var oversized = OsdLayoutHelper.Compute(8.0);
        Assert.Equal(new Size(1000, 340), oversized.FormSize);
    }

    [Fact]
    public void Compute_RoundsHalfPixelsAwayFromZero()
    {
        // 1.25 scale: 2 * 1.25 = 2.5 -> 3, 85 * 1.25 = 106.25 -> 106, 70 * 1.25 = 87.5 -> 88
        var layout = OsdLayoutHelper.Compute(1.25);

        Assert.Equal(new Size(313, 106), layout.FormSize);
        Assert.Equal(new Rectangle(30, 33, 40, 40), layout.IconBounds);
        Assert.Equal(new Rectangle(90, 28, 3, 50), layout.DividerRect);
        Assert.Equal(new Rectangle(113, 0, 165, 106), layout.TextRect);
    }
}
