using System.Globalization;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class UpperCaseConverterTests
{
    [Fact]
    public void Convert_TransformsStringsToUppercase()
    {
        var converter = new UpperCaseConverter();
        var result = converter.Convert("hello world", typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("HELLO WORLD", result);
    }

    [Fact]
    public void Convert_HandlesNullAndNumbersGracefully()
    {
        var converter = new UpperCaseConverter();
        Assert.Null(converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("165HZ", converter.Convert("165hz", typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("100", converter.Convert(100, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new UpperCaseConverter();
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack("TEST", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
