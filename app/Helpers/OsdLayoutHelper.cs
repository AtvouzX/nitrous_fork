using System;
using System.Drawing;

namespace Nitrous.Helpers;

/// <summary>
/// Pure layout metrics for the forged-metal OSD overlay. Base values are physical pixels
/// at 96 DPI (matching the reference mock: 400-600px wide, 85px tall, 19px chamfer) and are
/// scaled by the monitor DPI factor, because the form paints its own chrome and cannot rely on
/// WinForms auto-scaling. The form measures its two-line text with GDI+ and passes the wider
/// line as <c>contentWidth</c>; the helper only clamps and positions boxes.
/// </summary>
public static class OsdLayoutHelper
{
    public const int BaseHeight = 85;
    public const int BaseChamfer = 19;
    public const int BaseMinWidth = 250;
    public const int BaseMaxWidth = 600;
    public const int BasePaddingLeft = 24;
    public const int BasePaddingRight = 28;
    public const int BaseGap = 16;
    public const int BaseIconSize = 32;
    public const int BaseDividerWidth = 2;
    public const int BaseDividerHeight = 40;
    public const int BaseCornerAccentRun = 70;
    public const int BaseCornerAccentDrop = 35;
    public const int BaseContentGap = 2;
    public const int BaseBottomMargin = 160;

    // Never shrink below the design baseline; cap runaway DPI values.
    public const double MinScale = 1.0;
    public const double MaxScale = 4.0;

    public static OsdLayout Compute(double scale) => Compute(scale, 0);

    public static OsdLayout Compute(double scale, int contentWidth)
    {
        double s = Math.Clamp(scale, MinScale, MaxScale);

        int height = Scale(BaseHeight, s);
        int chamfer = Scale(BaseChamfer, s);
        int padLeft = Scale(BasePaddingLeft, s);
        int padRight = Scale(BasePaddingRight, s);
        int gap = Scale(BaseGap, s);
        int iconSize = Scale(BaseIconSize, s);
        int dividerWidth = Scale(BaseDividerWidth, s);
        int dividerHeight = Scale(BaseDividerHeight, s);
        int accentRun = Scale(BaseCornerAccentRun, s);
        int accentDrop = Scale(BaseCornerAccentDrop, s);
        int contentGap = Scale(BaseContentGap, s);
        int bottomMargin = Scale(BaseBottomMargin, s);

        int chrome = padLeft + iconSize + gap + dividerWidth + gap + padRight;
        int width = Math.Clamp(
            chrome + Math.Max(0, contentWidth),
            Scale(BaseMinWidth, s),
            Scale(BaseMaxWidth, s));

        var iconBounds = new Rectangle(padLeft, (height - iconSize) / 2, iconSize, iconSize);

        int dividerX = padLeft + iconSize + gap;
        var dividerRect = new Rectangle(dividerX, (height - dividerHeight) / 2, dividerWidth, dividerHeight);

        int textX = dividerX + dividerWidth + gap;
        var textRect = new Rectangle(textX, 0, Math.Max(0, width - textX - padRight), height);

        return new OsdLayout(
            new Size(width, height),
            chamfer,
            iconBounds,
            dividerRect,
            textRect,
            bottomMargin,
            accentRun,
            accentDrop,
            contentGap);
    }

    private static int Scale(int value, double scale) =>
        (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}

/// <summary>Scaled OSD geometry in physical pixels for the current monitor DPI.</summary>
public readonly record struct OsdLayout(
    Size FormSize,
    int Chamfer,
    Rectangle IconBounds,
    Rectangle DividerRect,
    Rectangle TextRect,
    int BottomMargin,
    int CornerAccentRun,
    int CornerAccentDrop,
    int ContentGap);
