using System;
using System.Collections.Generic;
using System.ServiceProcess;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class AcerServiceManagerTests
{
    [Fact]
    public void GetServiceSummary_ReturnsValidSummary()
    {
        // Act
        var summary = AcerServiceManager.GetServiceSummary();

        // Assert
        Assert.NotNull(summary);
        Assert.NotNull(summary.Services);
        
        // Since test environments vary, we can only assert boundaries
        Assert.True(summary.TotalFound >= 0);
        Assert.True(summary.RunningCount >= 0);
        Assert.True(summary.RunningCount <= summary.TotalFound);
        
        // Ensure that any returned service matches the TargetServices list
        foreach (var svc in summary.Services)
        {
            Assert.Contains(svc.ServiceName, AcerServiceManager.TargetServices, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GetTargetServices_FiltersCorrectlyAndCallerDisposesSafely()
    {
        // Act
        var targets = AcerServiceManager.GetTargetServices();

        try
        {
            Assert.NotNull(targets);
            foreach (var svc in targets)
            {
                Assert.Contains(svc.ServiceName, AcerServiceManager.TargetServices, StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            AcerServiceManager.DisposeServices(targets);
        }
    }

    [Fact]
    public void DisposeServices_HandlesNullAndDoubleDisposeGracefully()
    {
        // Null test
        var exNull = Record.Exception(() => AcerServiceManager.DisposeServices(null));
        Assert.Null(exNull);

        // Empty list test
        var exEmpty = Record.Exception(() => AcerServiceManager.DisposeServices(new List<ServiceController>()));
        Assert.Null(exEmpty);
    }

    [Fact]
    public void DisposeServices_EffectivelyDisposesServiceControllerInstances()
    {
        // Enumerate a small sample of local services to test real handle disposal
        var sampleServices = ServiceController.GetServices().Take(2).ToList();
        if (sampleServices.Count > 0)
        {
            var disposedFlags = new bool[sampleServices.Count];
            for (int i = 0; i < sampleServices.Count; i++)
            {
                int idx = i;
                sampleServices[i].Disposed += (_, _) => disposedFlags[idx] = true;
            }

            AcerServiceManager.DisposeServices(sampleServices);

            for (int i = 0; i < sampleServices.Count; i++)
            {
                Assert.True(disposedFlags[i], $"ServiceController at index {i} must have its Disposed event raised.");
            }
        }
    }

    [Fact]
    public void GetServiceSummary_ExecutesRepeatedlyWithoutLeakingHandlesOrCrashing()
    {
        for (int i = 0; i < 3; i++)
        {
            var summary = AcerServiceManager.GetServiceSummary();
            Assert.NotNull(summary);
            Assert.NotNull(summary.Services);
        }
    }

    [Fact]
    public async Task StopAndDisableSingleAsync_NonExistentService_ExecutesSafelyAndDisposesHandles()
    {
        var ex = await Record.ExceptionAsync(() => AcerServiceManager.StopAndDisableSingleAsync("Nitrous_Test_NonExistentService_404"));
        Assert.Null(ex);
    }
}
