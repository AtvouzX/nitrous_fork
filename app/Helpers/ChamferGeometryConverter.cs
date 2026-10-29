using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nitrous.Helpers;

/// <summary>
/// Multi-value converter that turns a rendered element size (ActualWidth, ActualHeight)
/// into the 6-point chamfered PathGeometry used by the shared tooltip shell, so plain
/// XAML tooltip templates can reuse ChamferGeometryHelper without code-behind.
/// ConverterParameter format: "chamfer;inset" (both optional, invariant culture).
/// </summary>
public sealed class ChamferGeometryConverter : IMultiValueConverter
{
    public object Convert(object[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: >= 2 } || values[0] is not double width || values[1] is not double height)
        {
            return Geometry.Empty;
        }

        if (width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        double chamfer = ChamferGeometryHelper.DefaultCardChamfer;
        double inset = 0.0;

        if (parameter is string spec && !string.IsNullOrWhiteSpace(spec))
        {
            string[] parts = spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var chamferParsed))
            {
                chamfer = chamferParsed;
            }
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var insetParsed))
            {
                inset = insetParsed;
            }
        }

        return ChamferGeometryHelper.CreateChamferPathGeometry(width, height, chamfer, inset);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => Array.Empty<object>();
}
