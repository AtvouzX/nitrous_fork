using System.Drawing;
using Nitrous.Enums;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class OsdThemeHelperTests
{
    [Fact]
    public void GetAccent_ReturnsReferenceColorPerProfile()
    {
        Assert.Equal(Color.FromArgb(0x4A, 0xA3, 0xBA), OsdThemeHelper.GetAccent(PowerProfile.Quiet));
        Assert.Equal(Color.FromArgb(0x5C, 0xAF, 0x6F), OsdThemeHelper.GetAccent(PowerProfile.Balanced));
        Assert.Equal(Color.FromArgb(0xE6, 0x75, 0x36), OsdThemeHelper.GetAccent(PowerProfile.Performance));
        Assert.Equal(Color.FromArgb(0xA8, 0x5D, 0xB0), OsdThemeHelper.GetAccent(PowerProfile.Turbo));
    }

    [Fact]
    public void GetAccent_UnknownProfile_FallsBackToWhite()
    {
        Assert.Equal(Color.White, OsdThemeHelper.GetAccent((PowerProfile)0xFF));
    }
}
