using System;
using System.Drawing;

namespace Nitrous.Helpers;

/// <summary>
/// Pure geometry helper that computes the chamfered polygon used by GDI+ (WinForms) surfaces
/// such as the OSD overlay, mirroring <see cref="ChamferGeometryHelper"/> for WPF.
/// Top-Left and Bottom-Right stay sharp; Top-Right and Bottom-Left are cut.
/// </summary>
public static class GdiChamferGeometryHelper
{
    /// <summary>
    /// Returns the six polygon points for a rectangle with top-right and bottom-left chamfer cuts.
    /// </summary>
    public static PointF[] CreateChamferPoints(RectangleF rect, float chamferSize)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return Array.Empty<PointF>();
        }

        float c = Math.Clamp(chamferSize, 0f, Math.Min(rect.Width, rect.Height) / 2f);

        return new[]
        {
            new PointF(rect.Left, rect.Top),
            new PointF(rect.Right - c, rect.Top),
            new PointF(rect.Right, rect.Top + c),
            new PointF(rect.Right, rect.Bottom),
            new PointF(rect.Left + c, rect.Bottom),
            new PointF(rect.Left, rect.Bottom - c)
        };
    }
}
