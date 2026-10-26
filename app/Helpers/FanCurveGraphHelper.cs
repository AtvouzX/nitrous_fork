using System;

namespace Nitrous.Helpers;

/// <summary>
/// Pure mathematical helper for fan curve coordinate conversion, grid snapping, and point constraint.
/// Independent of WPF or UI thread context for 100% testability.
/// </summary>
public static class FanCurveGraphHelper
{
    public const double DefaultMinTemp = 30.0;
    public const double DefaultMaxTemp = 100.0;
    public const double DefaultMinSpeed = 0.0;
    public const double DefaultMaxSpeed = 100.0;

    /// <summary>
    /// Translates temperature (°C) to canvas X pixel coordinate.
    /// </summary>
    public static double TempToCanvasX(double temp, double canvasWidth, double minTemp = DefaultMinTemp, double maxTemp = DefaultMaxTemp)
    {
        if (canvasWidth <= 0 || maxTemp <= minTemp) return 0.0;
        return ((temp - minTemp) / (maxTemp - minTemp)) * canvasWidth;
    }

    /// <summary>
    /// Translates canvas X pixel coordinate to temperature (°C).
    /// </summary>
    public static double CanvasXToTemp(double x, double canvasWidth, double minTemp = DefaultMinTemp, double maxTemp = DefaultMaxTemp)
    {
        if (canvasWidth <= 0) return minTemp;
        return (x / canvasWidth) * (maxTemp - minTemp) + minTemp;
    }

    /// <summary>
    /// Translates fan speed percentage to canvas Y pixel coordinate (inverted so 100% is at the top).
    /// </summary>
    public static double SpeedToCanvasY(double speed, double canvasHeight, double minSpeed = DefaultMinSpeed, double maxSpeed = DefaultMaxSpeed)
    {
        if (canvasHeight <= 0 || maxSpeed <= minSpeed) return 0.0;
        return canvasHeight - (((speed - minSpeed) / (maxSpeed - minSpeed)) * canvasHeight);
    }

    /// <summary>
    /// Translates canvas Y pixel coordinate to fan speed percentage.
    /// </summary>
    public static double CanvasYToSpeed(double y, double canvasHeight, double minSpeed = DefaultMinSpeed, double maxSpeed = DefaultMaxSpeed)
    {
        if (canvasHeight <= 0) return minSpeed;
        return ((canvasHeight - y) / canvasHeight) * (maxSpeed - minSpeed) + minSpeed;
    }

    /// <summary>
    /// Snaps a scalar value to the nearest grid step (e.g. 5.0).
    /// </summary>
    public static double SnapToGrid(double value, double step = 5.0)
    {
        if (step <= 0) return value;
        return Math.Round(value / step) * step;
    }

    /// <summary>
    /// Clamps temperature between its previous and next neighbor nodes plus/minus minGap.
    /// </summary>
    public static double ConstrainTemperature(
        double newTemp,
        double? prevNodeTemp,
        double? nextNodeTemp,
        double minTemp = DefaultMinTemp,
        double maxTemp = DefaultMaxTemp,
        double minGap = 5.0)
    {
        double lowerBound = prevNodeTemp.HasValue ? prevNodeTemp.Value + minGap : minTemp;
        double upperBound = nextNodeTemp.HasValue ? nextNodeTemp.Value - minGap : maxTemp;

        if (lowerBound > upperBound) lowerBound = upperBound;

        return Math.Clamp(newTemp, lowerBound, upperBound);
    }

    /// <summary>
    /// Clamps fan speed percentage within valid min and max boundaries.
    /// </summary>
    public static double ConstrainSpeed(double speed, double minSpeed = DefaultMinSpeed, double maxSpeed = DefaultMaxSpeed)
    {
        return Math.Clamp(speed, minSpeed, maxSpeed);
    }
}
