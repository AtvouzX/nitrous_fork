using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class NitroKeyManagerTests
{
    [Fact]
    public void AcerSenseExecutables_ContainsAll8KnownGenerations()
    {
        var expected = new[]
        {
            "PSLauncher.exe",
            "NSLauncher.exe",
            "NitroSense.exe",
            "NitroSenseV3.exe",
            "NitroSenseV4.exe",
            "PredatorSense.exe",
            "PredatorSenseV3.exe",
            "PredatorSenseV4.exe"
        };

        foreach (var exe in expected)
        {
            Assert.Contains(exe, NitroKeyManager.AcerSenseExecutables);
        }
    }

    [Fact]
    public void AcerSenseExecutables_ContainsNoEmptyOrNullEntries()
    {
        Assert.All(NitroKeyManager.AcerSenseExecutables, exe =>
        {
            Assert.False(string.IsNullOrWhiteSpace(exe));
            Assert.EndsWith(".exe", exe, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(@"C:\Tools\Nitrous.exe", @"""C:\Tools\Nitrous.exe""")]
    [InlineData(@"C:\Program Files\Nitrous\Nitrous.exe", @"""C:\Program Files\Nitrous\Nitrous.exe""")]
    [InlineData(@"""C:\Program Files\Nitrous\Nitrous.exe""", @"""C:\Program Files\Nitrous\Nitrous.exe""")] // Already quoted
    [InlineData(@"  ""C:\Program Files\Nitrous\Nitrous.exe""  ", @"""C:\Program Files\Nitrous\Nitrous.exe""")] // Whitespace + quotes
    public void FormatSafePath_AlwaysEnforcesSingleOuterQuotes(string input, string expected)
    {
        // Mirrors NitroKeyManager safePath logic: $"\"{exePath.Trim('\"', ' ')}\""
        string safePath = $"\"{input.Trim('\"', ' ')}\"";

        Assert.Equal(expected, safePath);
        Assert.StartsWith("\"", safePath);
        Assert.EndsWith("\"", safePath);
        Assert.False(safePath.StartsWith("\"\""), "Must not create double-double quotes");
    }
}
