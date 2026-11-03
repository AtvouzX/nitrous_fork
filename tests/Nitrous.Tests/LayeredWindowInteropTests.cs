using System.Runtime.InteropServices;
using Xunit;

namespace Nitrous.Tests;

public class LayeredWindowInteropTests
{
    [Fact]
    public void POINT_MatchesWin32Size()
    {
        Assert.Equal(8, Marshal.SizeOf<Nitrous.Interop.User32.POINT>());
    }

    [Fact]
    public void SIZE_MatchesWin32Size()
    {
        Assert.Equal(8, Marshal.SizeOf<Nitrous.Interop.User32.SIZE>());
    }

    [Fact]
    public void BLENDFUNCTION_MatchesWin32Size()
    {
        Assert.Equal(4, Marshal.SizeOf<Nitrous.Interop.User32.BLENDFUNCTION>());
    }
}
