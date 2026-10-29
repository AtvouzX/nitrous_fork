using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nitrous.Interop;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class BatteryManagerTests : IDisposable
{
    private readonly string _testLogDir;

    public BatteryManagerTests()
    {
        _testLogDir = Path.Combine(Path.GetTempPath(), "Nitrous_BatteryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testLogDir);
        LogManager.SetLogDirectoryForTesting(_testLogDir);
    }

    public void Dispose()
    {
        BatteryManager.SetPowerInfoDelegateForTesting(null);
        LogManager.SetLogDirectoryForTesting(null);
        try
        {
            if (Directory.Exists(_testLogDir))
            {
                Directory.Delete(_testLogDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void SystemBatteryState_SizeIs32()
    {
        int size = Marshal.SizeOf<Powrprof.SYSTEM_BATTERY_STATE>();
        Assert.Equal(32, size);
    }

    [Fact]
    public void GetBatteryState_DoesNotCrash()
    {
        var state = BatteryManager.GetBatteryState();
        // Just executing it to ensure P/Invoke definitions are valid.
        Assert.True(true);
    }

    [Fact]
    public void GetBatteryHealth_DoesNotCrash()
    {
        var health = BatteryManager.GetBatteryHealth();
        // This might be null if running on a desktop, or non-null on laptop.
        // The main point is to ensure it doesn't throw AccessViolationException or similar marshaling faults.
        Assert.True(true);
    }

    [Fact]
    public void GetBatteryState_WhenNtStatusFails_SuppressesRepetitiveWarningLogs()
    {
        BatteryManager.SetPowerInfoDelegateForTesting((int level, IntPtr inBuf, uint inLen, out Powrprof.SYSTEM_BATTERY_STATE outBuf, uint outLen) =>
        {
            outBuf = default;
            return 0xC0000001; // STATUS_UNSUCCESSFUL
        });

        // Simulate multiple recurring telemetry polling ticks
        for (int i = 0; i < 5; i++)
        {
            var state = BatteryManager.GetBatteryState();
            bool expectedAc = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
            Assert.Equal(expectedAc, state.AcOnLine);
        }

        string logPath = LogManager.LogFilePath;
        Assert.True(File.Exists(logPath));

        var lines = File.ReadAllLines(logPath)
            .Where(l => l.Contains("CallNtPowerInformation failed with NTSTATUS 0xC0000001"))
            .ToList();

        // Must be logged exactly once across 5 calls due to dampening
        Assert.Single(lines);
    }

    [Fact]
    public void GetBatteryState_WhenStatusTransitions_LogsNewWarning()
    {
        uint currentReturn = 0xC0000001;
        BatteryManager.SetPowerInfoDelegateForTesting((int level, IntPtr inBuf, uint inLen, out Powrprof.SYSTEM_BATTERY_STATE outBuf, uint outLen) =>
        {
            outBuf = default;
            return currentReturn;
        });

        BatteryManager.GetBatteryState();
        BatteryManager.GetBatteryState();

        // Transition to a different NTSTATUS
        currentReturn = 0xC0000034; // STATUS_OBJECT_NAME_NOT_FOUND
        BatteryManager.GetBatteryState();
        BatteryManager.GetBatteryState();

        string logPath = LogManager.LogFilePath;
        Assert.True(File.Exists(logPath));

        var lines01 = File.ReadAllLines(logPath)
            .Where(l => l.Contains("0xC0000001"))
            .ToList();
        var lines34 = File.ReadAllLines(logPath)
            .Where(l => l.Contains("0xC0000034"))
            .ToList();

        Assert.Single(lines01);
        Assert.Single(lines34);
    }

    [Fact]
    public void GetBatteryState_WhenStatusRecoversToSuccess_ResetsDampeningAndLogsDebug()
    {
        LogManager.IsDebugEnabled = true;
        uint currentReturn = 0xC0000001;
        BatteryManager.SetPowerInfoDelegateForTesting((int level, IntPtr inBuf, uint inLen, out Powrprof.SYSTEM_BATTERY_STATE outBuf, uint outLen) =>
        {
            outBuf = default;
            if (currentReturn == 0)
            {
                outBuf.AcOnLine = true;
                outBuf.BatteryPresent = true;
            }
            return currentReturn;
        });

        // 1. Initial failure
        BatteryManager.GetBatteryState();

        // 2. Recovery to STATUS_SUCCESS
        currentReturn = 0;
        var recoveredState = BatteryManager.GetBatteryState();
        Assert.True(recoveredState.AcOnLine);

        // 3. New failure after recovery
        currentReturn = 0xC0000001;
        BatteryManager.GetBatteryState();

        string logPath = LogManager.LogFilePath;
        Assert.True(File.Exists(logPath));

        string allLogs = File.ReadAllText(logPath);
        Assert.Contains("CallNtPowerInformation recovered to STATUS_SUCCESS", allLogs);

        var failLines = File.ReadAllLines(logPath)
            .Where(l => l.Contains("CallNtPowerInformation failed with NTSTATUS 0xC0000001"))
            .ToList();

        // Logged twice: once initially, and once after recovery
        Assert.Equal(2, failLines.Count);
    }
}
