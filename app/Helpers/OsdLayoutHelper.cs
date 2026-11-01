using System;
using System.Drawing;

namespace Nitrous.Helpers;

/// <summary>
/// Pure layout metrics for the OSD overlay. Base values are physical pixels at 96 DPI and are
/// scaled by the monitor DPI factor, because the form paints its own chrome and cannot rely on
/// WinForms auto-scaling. The font stays in points so GDI+ scales the text itself.
/// </summary>
public static class OsdLayoutHelper
{
    public const int BaseWidth = 280;
    public const int BaseHeight = 70;
    public const int BaseChamfer = 10;
    public const int BaseAccentBarWidth = 8;
    public const int BaseIconLeft = 25;
    public const int BaseIconTop = 17;
    public const int BaseIconSize = 36;
    public const int BaseTextLeft = 65;
    public const int BaseBottomMargin = 160;

    // Never shrink below the design baseline; cap runaway DPI values.
    public const double MinScale = 1.0;
    public const double MaxScale = 4.0;

    public static OsdLayout Compute(double scale)
    {
        double s = Math.Clamp(scale, MinScale, MaxScale);

        var formSize = new Size(Scale(BaseWidth, s), Scale(BaseHeight, s));
        int textLeft = Scale(BaseTextLeft, s);

        return new OsdLayout(
            formSize,
            Scale(BaseChamfer, s),
            Scale(BaseAccentBarWidth, s),
            new Rectangle(
                Scale(BaseIconLeft, s),
                Scale(BaseIconTop, s),
                Scale(BaseIconSize, s),
                Scale(BaseIconSize, s)),
            new Rectangle(textLeft, 0, formSize.Width - textLeft, formSize.Height),
            Scale(BaseBottomMargin, s));
    }

    private static int Scale(int value, double scale) =>
        (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}

/// <summary>Scaled OSD geometry in physical pixels for the current monitor DPI.</summary>
public readonly record struct OsdLayout(
    Size FormSize,
    int Chamfer,
    int AccentBarWidth,
    Rectangle IconBounds,
    Rectangle TextRect,
    int BottomMargin);
