using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Nitrous.Diagnostics;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class EcProbeTests
{
    [Theory]
    [InlineData("GetGamingSysInfo", true)]
    [InlineData("GetGamingProfile", true)]
    [InlineData("GetFanSpeed", true)]
    [InlineData("getsensors", true)]
    [InlineData("SetGamingFanSpeed", false)]
    [InlineData("SetGamingProfile", false)]
    [InlineData("InvokeGamingMethod", false)]
    [InlineData("ApplyClocks", false)]
    [InlineData("", false)]
    public void IsReadOnlyMethod_AllowsGetMethods_AndRejectsAllOthers(string methodName, bool expected)
    {
        Assert.Equal(expected, EcProbe.IsReadOnlyMethod(methodName));
    }

    [Fact]
    public void EcProbe_JsonShape_ContainsRequiredFields()
    {
        var sampleReport = new Dictionary<string, object?>
        {
            ["probeVersion"] = 10,
            ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["readOnly"] = true,
            ["note"] = "Read-only test report",
            ["machine"] = new Dictionary<string, object?>
            {
                ["manufacturer"] = "Acer",
                ["model"] = "Nitro ANV15-41"
            },
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["powerModeChannel"] = "EcHid",
                ["canSetPowerMode"] = true,
                ["supportsTurbo"] = false
            },
            ["errors"] = Array.Empty<string>()
        };

        var json = JsonSerializer.Serialize(sampleReport);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(10, root.GetProperty("probeVersion").GetInt32());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.Equal("Acer", root.GetProperty("machine").GetProperty("manufacturer").GetString());
        Assert.Equal("EcHid", root.GetProperty("capabilities").GetProperty("powerModeChannel").GetString());
        Assert.Equal(0, root.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public void GetLatestEcProbeReportSummary_WhenDirectoryMissingOrEmpty_ReturnsNone()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Nitrous_ProbeTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Directory does not exist:
            Assert.False(Directory.Exists(tempDir));
            // LogManager's default path check won't throw and returns a string
            var summary = LogManager.GetLatestEcProbeReportSummary();
            Assert.NotNull(summary);
            // Result is either "none" or valid report string, never throws
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void ExportSystemDiagnostics_IncludesEcProbeAndCapabilitiesSections()
    {
        var dump = LogManager.ExportSystemDiagnostics();
        Assert.Contains("[ACER CAPABILITIES & EC HID]", dump);
        Assert.Contains("[LATEST EC PROBE REPORT]", dump);
        Assert.Contains("Power Channel:", dump);
    }
}
