using System;

namespace Nitrous.Helpers;

/// <summary>
/// Pure window-placement math: keep popups fully inside the working area.
/// </summary>
public static class WindowPlacementHelper
{
    public const double DashboardRightMargin = 12;
    public const double DashboardBottomMargin = 50;

    /// <summary>
    /// Docks the dashboard bottom-right inside the given working area
    /// (taskbar already excluded), clamping fully on-screen.
    /// </summary>
    public static (double Left, double Top) ComputeDashboardDock(
        double areaLeft, double areaTop, double areaRight, double areaBottom,
        double dashWidth, double dashHeight)
    {
        double left = areaRight - dashWidth - DashboardRightMargin;
        double top = areaBottom - dashHeight - DashboardBottomMargin;
        left = Math.Clamp(left, areaLeft, Math.Max(areaLeft, areaRight - dashWidth));
        top = Math.Clamp(top, areaTop, Math.Max(areaTop, areaBottom - dashHeight));
        return (left, top);
    }

    public static (double Left, double Top) ComputeCurveEditorPosition(
        double dashLeft, double dashTop, double dashWidth, double dashHeight,
        double curveWidth, double curveHeight,
        double areaLeft, double areaTop, double areaRight, double areaBottom)
    {
        double left = dashLeft - curveWidth - 10;
        if (left < areaLeft || left + curveWidth > areaRight)
            left = dashLeft + dashWidth + 10;
        left = Math.Clamp(left, areaLeft, Math.Max(areaLeft, areaRight - curveWidth));

        // Bottom-align with dashboard so the taller editor never hangs off-screen.
        double top = dashTop + dashHeight - curveHeight;
        top = Math.Clamp(top, areaTop, Math.Max(areaTop, areaBottom - curveHeight));
        return (left, top);
    }
}
