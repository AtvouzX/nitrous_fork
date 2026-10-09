using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class LogManagerTests : IDisposable
{
    private readonly string _testDir;

    public LogManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Nitrous_LogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        LogManager.SetLogDirectoryForTesting(_testDir);
    }

    public void Dispose()
    {
        LogManager.SetLogDirectoryForTesting(null);
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void LogManager_WritesFormattedInfoEntry()
    {
        LogManager.IsDebugEnabled = false;
        LogManager.Info("Engine started successfully", "Core");

        string logPath = LogManager.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        Assert.Contains("[INF]", content);
        Assert.Contains("[Core]", content);
        Assert.Contains("Engine started successfully", content);
    }

    [Fact]
    public void LogManager_WhenDebugDisabled_DoesNotWriteDebugEntries()
    {
        LogManager.IsDebugEnabled = false;
        LogManager.Debug("Detailed register probe 0x0A = 0x3F", "EC");

        string logPath = LogManager.LogFilePath;
        if (File.Exists(logPath))
        {
            string content = File.ReadAllText(logPath);
            Assert.DoesNotContain("Detailed register probe", content);
        }
    }

    [Fact]
    public void LogManager_WhenDebugEnabled_WritesDebugEntries()
    {
        LogManager.IsDebugEnabled = true;
        LogManager.Debug("Detailed register probe 0x0A = 0x3F", "EC");

        string logPath = LogManager.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        Assert.Contains("[DBG]", content);
        Assert.Contains("[EC]", content);
        Assert.Contains("Detailed register probe 0x0A = 0x3F", content);
    }

    [Fact]
    public void LogManager_WritesErrorWithExceptionDetails()
    {
        var ex = new InvalidOperationException("WMI connection timed out");
        LogManager.Error("Failed to probe sensors", ex, "WMI");

        string logPath = LogManager.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        Assert.Contains("[ERR]", content);
        Assert.Contains("[WMI]", content);
        Assert.Contains("Failed to probe sensors", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("WMI connection timed out", content);
    }

    [Fact]
    public void LogManager_HandlesConcurrentWritersWithoutLockExceptions()
    {
        LogManager.IsDebugEnabled = true;
        const int threadCount = 10;
        const int writesPerThread = 50;

        Parallel.For(0, threadCount, i =>
        {
            for (int j = 0; j < writesPerThread; j++)
            {
                LogManager.Info($"Thread {i} log iteration {j}", "ConcurrencyTest");
            }
        });

        string logPath = LogManager.LogFilePath;
        Assert.True(File.Exists(logPath));

        string[] lines = File.ReadAllLines(logPath);
        int matchingLines = lines.Count(l => l.Contains("[ConcurrencyTest]"));
        Assert.Equal(threadCount * writesPerThread, matchingLines);
    }

    [Fact]
    public void LogManager_RotatesWhenSizeExceedsLimit()
    {
        string logPath = LogManager.LogFilePath;
        string rotatedPath = LogManager.RotatedLogFilePath;

        // Pre-create file with size exceeding MaxLogFileSizeBytes
        byte[] dummyData = new byte[LogManager.MaxLogFileSizeBytes + 1024];
        File.WriteAllBytes(logPath, dummyData);

        // Next write should trigger rotation
        LogManager.Info("Triggering rotation with new entry", "RotationTest");

        Assert.True(File.Exists(rotatedPath), "Previous log should be rotated to .old");
        Assert.True(File.Exists(logPath), "New active log should be created");

        string newContent = File.ReadAllText(logPath);
        Assert.Contains("Triggering rotation with new entry", newContent);
    }

    [Fact]
    public void ExportSystemDiagnostics_GeneratesReportWithoutThrowing()
    {
        string report = LogManager.ExportSystemDiagnostics();

        Assert.NotNull(report);
        Assert.NotEmpty(report);
        Assert.Contains("NITROUS DIAGNOSTIC HARDWARE DUMP", report);
        Assert.Contains("App Version:", report);
        Assert.Contains("[HARDWARE & ACER EC]", report);
        Assert.Contains("[DISCRETE GPU]", report);
        Assert.Contains("[POWER & BATTERY]", report);
    }

    [Fact]
    public void LogManager_SyncSettings_ReadsRegistryDebuggingFlag()
    {
        try
        {
            SettingsManager.Save("Debugging", true);
            LogManager.SyncSettings();
            Assert.True(LogManager.IsDebugEnabled);

            SettingsManager.Save("Debugging", false);
            LogManager.SyncSettings();
            Assert.False(LogManager.IsDebugEnabled);
        }
        finally
        {
            SettingsManager.Delete("Debugging");
            SettingsManager.Delete("DebugLogging");
            LogManager.IsDebugEnabled = false;
        }
    }

    [Fact]
    public void LogManager_SyncSettings_ReadsEnvironmentVariable()
    {
        string? prevEnv = Environment.GetEnvironmentVariable("NITROUS_DEBUG");
        try
        {
            Environment.SetEnvironmentVariable("NITROUS_DEBUG", "1");
            LogManager.SyncSettings();
            Assert.True(LogManager.IsDebugEnabled);

            Environment.SetEnvironmentVariable("NITROUS_DEBUG", "0");
            LogManager.SyncSettings();
            Assert.False(LogManager.IsDebugEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NITROUS_DEBUG", prevEnv);
            LogManager.IsDebugEnabled = false;
        }
    }
}
