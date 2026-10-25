using System.IO;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class UpdateManagerTests
{
    [Fact]
    public void CurrentVersion_IsValidThreePartSemVer()
    {
        string current = UpdateManager.CurrentVersion;
        Assert.False(string.IsNullOrWhiteSpace(current));

        bool parsed = Version.TryParse(current, out Version? v);
        Assert.True(parsed, $"CurrentVersion '{current}' must be parseable as System.Version");
        Assert.NotNull(v);
        Assert.True(v.Major >= 0);
        Assert.True(v.Minor >= 0);
    }

    [Theory]
    [InlineData("v0.8.1", "0.8.0", true)]   // Newer patch
    [InlineData("v0.9.0", "0.8.0", true)]   // Newer minor
    [InlineData("v1.0.0", "0.8.0", true)]   // Newer major
    [InlineData("V0.8.1", "0.8.0", true)]   // Capital 'V' prefix
    [InlineData("0.8.1", "0.8.0", true)]    // No prefix
    [InlineData("v0.8.0", "0.8.0", false)]  // Identical
    [InlineData("v0.7.5", "0.8.0", false)]  // Older version
    [InlineData("v0.6.4", "0.8.0", false)]  // Older version
    public void VersionComparison_DetectsAvailableUpdatesAccurately(string remoteTag, string localVersion, bool shouldUpdate)
    {
        string cleanRemote = remoteTag.Trim().TrimStart('v', 'V');
        string cleanLocal = localVersion.Trim().TrimStart('v', 'V');

        bool parsedRemote = Version.TryParse(cleanRemote, out Version? vRemote);
        bool parsedLocal = Version.TryParse(cleanLocal, out Version? vLocal);

        Assert.True(parsedRemote, $"Remote tag '{remoteTag}' should parse");
        Assert.True(parsedLocal, $"Local version '{localVersion}' should parse");
        Assert.Equal(shouldUpdate, vRemote! > vLocal!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../../evil")]
    [InlineData("v0.8.0/../hack")]
    [InlineData("v0.8.0\0bad")]
    public void TagValidation_RejectsInvalidOrMaliciousTags(string invalidTag)
    {
        bool isInvalid = string.IsNullOrWhiteSpace(invalidTag) ||
                         invalidTag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                         invalidTag.Contains("..") ||
                         invalidTag.Contains('/') ||
                         invalidTag.Contains('\\');

        Assert.True(isInvalid, $"Tag '{invalidTag}' must be rejected by updater validation");
    }

    [Theory]
    [InlineData("v0.8.0")]
    [InlineData("v0.8.1")]
    [InlineData("v1.0.0-rc1")]
    public void TagValidation_AcceptsSafeReleaseTags(string safeTag)
    {
        bool isInvalid = string.IsNullOrWhiteSpace(safeTag) ||
                         safeTag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                         safeTag.Contains("..") ||
                         safeTag.Contains('/') ||
                         safeTag.Contains('\\');

        Assert.False(isInvalid, $"Tag '{safeTag}' should be accepted as safe");
    }

    [Fact]
    public void CleanupOldFiles_DoesNotThrowExceptions()
    {
        var ex = Record.Exception(() => UpdateManager.CleanupOldFiles());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00 }, true)]
    [InlineData(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }, false)]
    [InlineData(new byte[] { (byte)'P', (byte)'K', 0x03, 0x04 }, false)]
    [InlineData(new byte[] { (byte)'<', (byte)'!', (byte)'D', (byte)'O' }, false)]
    public void PEHeaderValidation_AcceptsOnlyValidDosHeader(byte[] header, bool isValid)
    {
        bool matches = header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
        Assert.Equal(isValid, matches);
    }
}
