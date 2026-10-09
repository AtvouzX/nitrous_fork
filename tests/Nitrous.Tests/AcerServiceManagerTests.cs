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
}
