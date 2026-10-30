using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class WindowPlacementHelperTests
{
    private const double AreaL = 0, AreaT = 0, AreaR = 1920, AreaB = 1040;

    [Fact]
    public void Docks_BottomRight_AboveTaskbar()
    {
        var (left, top) = WindowPlacementHelper.ComputeDashboardDock(AreaL, AreaT, AreaR, AreaB, 375, 580);
        Assert.Equal(1920 - 375 - 12, left);
        Assert.Equal(1040 - 580 - 50, top);
    }

    [Fact]
    public void Dock_ClampsInside_SmallWorkArea()
    {
        var (left, top) = WindowPlacementHelper.ComputeDashboardDock(0, 0, 300, 400, 375, 580);
        Assert.Equal(0, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void PlacesLeftOfDashboard_BottomAligned()
    {
        var (left, top) = WindowPlacementHelper.ComputeCurveEditorPosition(
            1400, 410, 375, 580, 880, 660, AreaL, AreaT, AreaR, AreaB);
        Assert.Equal(1400 - 880 - 10, left);
        Assert.Equal(410 + 580 - 660, top); // bottom edges flush
    }

    [Fact]
    public void FallsBackRight_WhenNoRoomOnLeft()
    {
        var (left, _) = WindowPlacementHelper.ComputeCurveEditorPosition(
            100, 410, 375, 580, 880, 660, AreaL, AreaT, AreaR, AreaB);
        Assert.Equal(100 + 375 + 10, left);
    }

    [Fact]
    public void ClampsTop_WhenDashboardNearTaskbar()
    {
        // Dashboard bottom near work-area bottom: editor bottom aligns, never hangs below.
        var (left, top) = WindowPlacementHelper.ComputeCurveEditorPosition(
            1400, 500, 375, 540, 880, 660, AreaL, AreaT, AreaR, AreaB);
        Assert.Equal(1040 - 660, top);
        Assert.True(left + 880 <= AreaR);
    }

    [Fact]
    public void ClampsLeft_WhenFallbackOverflowsRight()
    {
        var (left, _) = WindowPlacementHelper.ComputeCurveEditorPosition(
            700, 410, 375, 580, 880, 660, AreaL, AreaT, 1900, AreaB);
        Assert.Equal(1900 - 880, left);
    }
}
