using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class HotkeyFormatterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("1")]
    [InlineData("1|2|3")]
    [InlineData("abc|def")]
    public void Format_WithNullEmptyOrMalformedInput_ReturnsNone(string? input)
    {
        string result = HotkeyFormatter.Format(input);
        Assert.Equal("None", result);
    }

    [Theory]
    [InlineData("2|81", "Ctrl + Q")]                    // ModCtrl = 2, VK_Q = 81
    [InlineData("4|81", "Shift + Q")]                   // ModShift = 4
    [InlineData("1|81", "Alt + Q")]                     // ModAlt = 1
    [InlineData("6|68", "Ctrl + Shift + D")]            // Ctrl + Shift, VK_D = 68
    [InlineData("7|80", "Ctrl + Shift + Alt + P")]      // Ctrl + Shift + Alt, VK_P = 80
    [InlineData("0|112", "F1")]                         // No modifiers, VK_F1 = 112
    public void Format_WithValidSerializedHotkey_ProducesExpectedLabel(string serialized, string expectedLabel)
    {
        string label = HotkeyFormatter.Format(serialized);
        Assert.Equal(expectedLabel, label);
    }

    [Fact]
    public void Format_WithExplicitModifiersAndVk_ProducesExpectedLabel()
    {
        string label = HotkeyFormatter.Format(HotkeyFormatter.ModCtrl | HotkeyFormatter.ModAlt, 83); // S = 83
        Assert.Equal("Ctrl + Alt + S", label);
    }

    [Theory]
    [InlineData("2|81", 2u, 81u)]
    [InlineData("7|120", 7u, 120u)]
    [InlineData("0|65", 0u, 65u)]
    public void TryParse_WithValidFormat_ExtractsModifiersAndKey(string input, uint expectedMods, uint expectedVk)
    {
        bool success = HotkeyFormatter.TryParse(input, out uint mods, out uint vk);
        Assert.True(success);
        Assert.Equal(expectedMods, mods);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2")]
    [InlineData("2|81|extra")]
    [InlineData("notanumber|81")]
    [InlineData("2|notanumber")]
    public void TryParse_WithMalformedInput_ReturnsFalse(string? input)
    {
        bool success = HotkeyFormatter.TryParse(input, out uint mods, out uint vk);
        Assert.False(success);
        Assert.Equal(0u, mods);
        Assert.Equal(0u, vk);
    }

    [Fact]
    public void Serialize_ProducesStandardPipeDelimitedFormat()
    {
        string serialized = HotkeyFormatter.Serialize(6, 68);
        Assert.Equal("6|68", serialized);
    }

    [Fact]
    public void HotkeyRoundtrip_SerializeThenParseThenFormat_IsLossless()
    {
        uint originalMods = HotkeyFormatter.ModCtrl | HotkeyFormatter.ModShift;
        uint originalVk = 81; // Q

        string serialized = HotkeyFormatter.Serialize(originalMods, originalVk);
        bool parsed = HotkeyFormatter.TryParse(serialized, out uint parsedMods, out uint parsedVk);
        string formatted = HotkeyFormatter.Format(serialized);

        Assert.True(parsed);
        Assert.Equal(originalMods, parsedMods);
        Assert.Equal(originalVk, parsedVk);
        Assert.Equal("Ctrl + Shift + Q", formatted);
    }
}

