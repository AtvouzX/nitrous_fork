using System;
using System.Globalization;
using System.Windows.Data;

namespace Nitrous.Helpers;

/// <summary>
/// Value converter that transforms text to uppercase for Nitrous Ignite tactical typography.
/// </summary>
[ValueConversion(typeof(object), typeof(string))]
public sealed class UpperCaseConverter : IValueConverter
{
    public static readonly UpperCaseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToUpper(culture ?? CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("UpperCaseConverter does not support two-way convert back.");
    }
}
