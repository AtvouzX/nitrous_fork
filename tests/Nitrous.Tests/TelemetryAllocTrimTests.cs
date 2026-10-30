using Nitrous.Helpers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

// Ticket 8 RED: alloc-trim helpers must be behavior-identical pure seams.
public class TelemetryAllocTrimTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", "NVIDIA GEFORCE RTX 4060 LAPTOP")]
    [InlineData("GeForce RTX 3070 Ti Laptop GPU", "GEFORCE RTX 3070 TI LAPTOP")]
    [InlineData("NVIDIA RTX A2000", "NVIDIA RTX A2000")]
    [InlineData("", "NVIDIA GPU")]
    public void GetCachedGpuDisplayName_FormatsUpperTrimmed(string raw, string expected)
    {
        using var vm = new DashboardViewModel();
        Assert.Equal(expected, vm.GetCachedGpuDisplayName(raw));
    }

    [Fact]
    public void GetCachedGpuDisplayName_SameRaw_ReturnsSameReference()
    {
        using var vm = new DashboardViewModel();
        string first = vm.GetCachedGpuDisplayName("NVIDIA GeForce RTX 4060 Laptop GPU");
        string second = vm.GetCachedGpuDisplayName("NVIDIA GeForce RTX 4060 Laptop GPU");
        Assert.Same(first, second);
    }

    [Theory]
    [InlineData("Ada Lovelace", "ADA LOVELACE")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void GetCachedGpuArchDisplay_FormatsUpper(string? raw, string expected)
    {
        using var vm = new DashboardViewModel();
        Assert.Equal(expected, vm.GetCachedGpuArchDisplay(raw));
    }

    [Fact]
    public void GetCachedGpuArchDisplay_SameRaw_ReturnsSameReference()
    {
        using var vm = new DashboardViewModel();
        string first = vm.GetCachedGpuArchDisplay("Ada Lovelace");
        string second = vm.GetCachedGpuArchDisplay("Ada Lovelace");
        Assert.Same(first, second);
    }

    [Fact]
    public void TrayTooltipInputsChanged_SameInputs_ReturnsFalse()
    {
        Assert.False(TrayApplication.TooltipInputsChanged(48, 42, true, 48, 42, true));
    }

    [Fact]
    public void TrayTooltipInputsChanged_DifferentInputs_ReturnsTrue()
    {
        Assert.True(TrayApplication.TooltipInputsChanged(48, 42, true, 49, 42, true));
        Assert.True(TrayApplication.TooltipInputsChanged(48, 42, true, 48, 43, true));
        Assert.True(TrayApplication.TooltipInputsChanged(48, 42, true, 48, 42, false));
    }

    [Fact]
    public void GenerateGraphPoints_WhenHistoryEmpty_ReusesSharedInstance()
    {
        var a = GraphGeometryHelper.GenerateGraphPoints(
            System.Array.Empty<DashboardViewModel.TelemetryPoint>(), true, 1000000);
        var b = GraphGeometryHelper.GenerateGraphPoints(
            System.Array.Empty<DashboardViewModel.TelemetryPoint>(), false, 1000000);
        Assert.Same(a, b);
    }

    [Fact]
    public void GenerateGraphFillPoints_WhenHistoryEmpty_ReusesSharedInstance()
    {
        var a = GraphGeometryHelper.GenerateGraphFillPoints(
            System.Array.Empty<DashboardViewModel.TelemetryPoint>(), true, 1000000);
        var b = GraphGeometryHelper.GenerateGraphFillPoints(
            System.Array.Empty<DashboardViewModel.TelemetryPoint>(), false, 1000000);
        Assert.Same(a, b);
    }
}
