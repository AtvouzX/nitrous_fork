using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class NvidiaGpuManagerTests
{
    [Fact]
    public void NvidiaGpuManager_IsGpuAwake_ReturnsBooleanWithoutExceptions()
    {
        // This is a direct test of the CM API interop architecture.
        // It verifies that IsGpuAwake can be called safely without crashing.
        var isAwake = NvidiaGpuManager.IsGpuAwake();
        
        Assert.True(isAwake || !isAwake); // Just asserting no exceptions were thrown
    }
    
    [Fact]
    public async Task NvidiaGpuManager_GetNvmlTelemetryAsync_DoesNotThrowWhenCalled()
    {
        var manager = new NvidiaGpuManager();
        var token = new CancellationToken();
        
        // This validates the NVML architecture loads properly (or fails gracefully if NVML missing).
        var telemetry = await manager.GetNvmlTelemetryAsync(token);
        
        // If the system has an awake Nvidia GPU, it should return a non-null object.
        // If it's asleep or not Nvidia, it returns an object with PState = "Sleep" or handles the fallback.
        Assert.NotNull(telemetry);
    }

    [Fact]
    public void IsOnlyNitrousRunningOnGpu_ReturnsTrue_WhenOnlyNitrousIsRunning()
    {
        int nitrousPid = 1234;
        var graphicsInfos = new NvidiaGpuManager.NvmlProcessInfo[] { new() { Pid = 1234 } };
        var computeInfos = Array.Empty<NvidiaGpuManager.NvmlProcessInfo>();

        bool result = NvidiaGpuManager.IsOnlyNitrousRunningOnGpu(
            graphicsInfos, 1,
            computeInfos, 0,
            nitrousPid);

        Assert.True(result);
    }

    [Fact]
    public void IsOnlyNitrousRunningOnGpu_ReturnsFalse_WhenOtherAppsAreRunning()
    {
        int nitrousPid = 1234;
        var graphicsInfos = new NvidiaGpuManager.NvmlProcessInfo[] { new() { Pid = 1234 }, new() { Pid = 9999 } };
        
        bool result = NvidiaGpuManager.IsOnlyNitrousRunningOnGpu(
            graphicsInfos, 2,
            null, 0,
            nitrousPid);

        Assert.False(result);
    }

    [Fact]
    public void IsOnlyNitrousRunningOnGpu_ReturnsFalse_WhenNoAppsAreRunning()
    {
        int nitrousPid = 1234;
        var graphicsInfos = Array.Empty<NvidiaGpuManager.NvmlProcessInfo>();
        
        bool result = NvidiaGpuManager.IsOnlyNitrousRunningOnGpu(
            graphicsInfos, 0,
            null, 0,
            nitrousPid);

        Assert.False(result);
    }
}
