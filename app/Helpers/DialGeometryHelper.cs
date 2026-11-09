using System;
using Nitrous.Enums;

namespace Nitrous.Helpers;

/// <summary>
/// Pure mathematical helper to compute dial angles and needle polar-to-cartesian coordinates.
/// Decoupled from Windows Forms or WPF rendering for 100% testability.
/// </summary>
public static class DialGeometryHelper
{
    public const double ArcStartAngle = 135.0;
    public const double ArcSweepAngle = 270.0;

    /// <summary>
    /// Returns the dial needle rotation angle in degrees for the given power profile.
    /// </summary>
    public static double GetNeedleAngle(PowerProfile profile) => profile switch
    {
        PowerProfile.Eco => 135.0,
        PowerProfile.Quiet => 180.0,
        PowerProfile.Balanced => 270.0,
        PowerProfile.Performance => 360.0,
        PowerProfile.Turbo => 45.0,
        _ => 270.0
    };

    /// <summary>
    /// Converts polar coordinates (radius, angle in degrees) centered at (centerX, centerY)
    /// to 2D integer Cartesian coordinates (X, Y).
    /// </summary>
    public static (int X, int Y) PolarToCartesian(int centerX, int centerY, double radius, double angleDegrees)
    {
        double rad = angleDegrees * Math.PI / 180.0;
        int x = centerX + (int)Math.Round(radius * Math.Cos(rad));
        int y = centerY + (int)Math.Round(radius * Math.Sin(rad));
        return (x, y);
    }

    /// <summary>
    /// Calculates the needle tip coordinates for the given dial bounds and power profile.
    /// </summary>
    public static (int X, int Y) CalculateNeedleTip(int centerX, int centerY, double radius, PowerProfile profile)
    {
        double angle = GetNeedleAngle(profile);
        return PolarToCartesian(centerX, centerY, radius, angle);
    }
}

