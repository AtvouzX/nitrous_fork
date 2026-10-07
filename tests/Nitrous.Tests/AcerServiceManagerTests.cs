using System;
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
}
