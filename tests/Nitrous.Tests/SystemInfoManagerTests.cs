using System;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class SystemInfoManagerTests
{
    [Fact]
    public void GetSystemModel_ReturnsNonEmptyString()
    {
        string model = SystemInfoManager.GetSystemModel();
        Assert.NotNull(model);
        Assert.NotEmpty(model);
    }

    [Fact]
    public void GetSystemManufacturer_ReturnsNonEmptyString()
    {
        string mfg = SystemInfoManager.GetSystemManufacturer();
        Assert.NotNull(mfg);
        Assert.NotEmpty(mfg);
    }

    [Fact]
    public void GetBaseBoardProduct_ReturnsNonEmptyString()
    {
        string board = SystemInfoManager.GetBaseBoardProduct();
        Assert.NotNull(board);
        Assert.NotEmpty(board);
    }

    [Fact]
    public void GetBiosVersion_ReturnsNonEmptyString()
    {
        string bios = SystemInfoManager.GetBiosVersion();
        Assert.NotNull(bios);
        Assert.NotEmpty(bios);
    }

    [Fact]
    public void GetBiosReleaseDate_ReturnsNonEmptyString()
    {
        string date = SystemInfoManager.GetBiosReleaseDate();
        Assert.NotNull(date);
        Assert.NotEmpty(date);
    }

    [Fact]
    public void GetCpuModel_ReturnsNonEmptyString()
    {
        string cpu = SystemInfoManager.GetCpuModel();
        Assert.NotNull(cpu);
        Assert.NotEmpty(cpu);
    }

    [Fact]
    public void GetDiscreteGpuInfo_ReturnsValidTupleWithoutThrowing()
    {
        var (name, driver) = SystemInfoManager.GetDiscreteGpuInfo();
        Assert.NotNull(name);
        Assert.NotEmpty(name);
        Assert.NotNull(driver);
        Assert.NotEmpty(driver);
    }
}
