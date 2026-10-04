using System.IO;
using System.Reflection;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Ui;
using Xunit;

namespace Nitrous.Tests;

public class PerformanceAuditTests
{
    // =========================================================================
    // 1. Idle Working Set (Memory Footprint) Invariants
    // =========================================================================

    [Fact]
    public void TrimWorkingSet_ExecutesSafelyAndTrimsMemory()
    {
        // Must execute cleanly without throwing exceptions even in non-elevated or test environments
        var exception = Record.Exception(() => MemoryHelper.TrimWorkingSet());
        Assert.Null(exception);
    }

    [Fact]
    public void DashboardViewModel_ImplementsIDisposable()
    {
        // Transient UI requirement: DashboardViewModel must implement IDisposable
        // to deterministically release timers, cancellation tokens, and unmanaged resources on close
        Assert.True(
            typeof(IDisposable).IsAssignableFrom(typeof(DashboardViewModel)),
            "DashboardViewModel must implement IDisposable for deterministic cleanup in the transient UI architecture."
        );
    }

    [Fact]
    public void DashboardViewModel_PauseAndResumePolling_ManagesTelemetryLifecycle()
    {
        using var vm = new DashboardViewModel();

        // 1. Initially active upon startup
        Assert.True(vm.IsPollingActive, "Telemetry polling must be active upon ViewModel creation.");

        // 2. When dashboard is minimized or hidden, polling must be paused (cancelled)
        vm.PausePolling();
        Assert.False(vm.IsPollingActive, "Telemetry polling must be cancelled when PausePolling is invoked.");

        // 3. When restored to foreground, polling resumes
        vm.ResumePolling();
        Assert.True(vm.IsPollingActive, "Telemetry polling must resume when ResumePolling is invoked.");

        // 4. On disposal, polling token must be cancelled and cleaned up
        vm.Dispose();
        Assert.False(vm.IsPollingActive, "Telemetry polling must be cancelled on Dispose.");
    }

    [Fact]
    public void DashboardViewModel_ClearDeepTelemetryUI_ResetsStateToSleeping()
    {
        using var vm = new DashboardViewModel();

        // Simulate active state
        vm.GpuNameText = "NVIDIA GeForce RTX 4060";
        vm.GpuArchText = "Ada Lovelace";
        vm.GpuLoadText = "85 %";
        vm.GpuDeepTempText = "68 C";
        vm.GpuPStateText = "P0";
        vm.GpuCoreClockText = "2450 MHz";
        vm.GpuMemClockText = "8000 MHz";
        vm.GpuPowerText = "115.0 W";

        // Invoke ClearDeepTelemetryUI
        vm.ClearDeepTelemetryUI();

        Assert.Equal("NVIDIA GPU (SLEEPING)", vm.GpuNameText);
        Assert.Equal("", vm.GpuArchText);
        Assert.Equal("-- %", vm.GpuLoadText);
        Assert.Equal("-- C", vm.GpuDeepTempText);
        Assert.Equal("--", vm.GpuPStateText);
        Assert.Equal("-- MHz", vm.GpuCoreClockText);
        Assert.Equal("-- MHz", vm.GpuMemClockText);
        Assert.Equal("-- W", vm.GpuPowerText);
    }

    // =========================================================================
    // 2. Discrete GPU (dGPU) Sleep & Optimus Preservation Invariants
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-20)]
    public void FormatTrayTooltip_WhenGpuIsZeroOrNegative_DisplaysSleepAndSuppressesWakeup(int sleepingGpuTemp)
    {
        // Core Battery Invariant: An EC reading of 0°C (or <= 0) indicates dGPU is in D3Cold sleep.
        // Nitrous must display "Sleep" in the tray tooltip without querying NVML or NvAPI.
        string tooltip = TrayApplication.FormatTrayTooltip(48, sleepingGpuTemp);

        Assert.Contains("GPU: Sleep", tooltip);
        Assert.DoesNotContain("0°C", tooltip);
    }

    [Theory]
    [InlineData(35, 42)]
    [InlineData(50, 65)]
    [InlineData(85, 78)]
    public void FormatTrayTooltip_WhenGpuIsActive_DisplaysGpuTemperature(int cpuTemp, int gpuTemp)
    {
        string tooltip = TrayApplication.FormatTrayTooltip(cpuTemp, gpuTemp);

        Assert.Contains($"CPU: {cpuTemp}°C", tooltip);
        Assert.Contains($"GPU: {gpuTemp}°C", tooltip);
        Assert.DoesNotContain("Sleep", tooltip);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-999, -999)]
    [InlineData(100, 100)]
    [InlineData(9999, 9999)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void FormatTrayTooltip_NeverExceedsWinFormsNotifyIconCharLimit(int cpuTemp, int gpuTemp)
    {
        // Windows Shell NotifyIcon.Text throws an ArgumentException if length > 63 characters
        string tooltip = TrayApplication.FormatTrayTooltip(cpuTemp, gpuTemp);

        Assert.True(tooltip.Length <= 63, $"Tray tooltip length ({tooltip.Length}) must not exceed 63 characters.");
    }

    [Fact]
    public void AcerWmiManager_SensorAddresses_MatchAcerEcHardwareSpecification()
    {
        // Acer EC Embedded Controller Hardware Sensor Register Addresses:
        // CPU Temp: 0x0101
        // CPU Fan:  0x0201
        // GPU Temp: 0x0A01 (with 0x0901 & 0x0B01 as legacy/BIOS fallbacks)
        // GPU Fan:  0x0601
        Assert.Equal(0x0101u, AcerWmiManager.SensorCpuTemp);
        Assert.Equal(0x0201u, AcerWmiManager.SensorCpuRpm);
        Assert.Equal(0x0A01u, AcerWmiManager.SensorGpuTempPrimary);
        Assert.Equal(0x0901u, AcerWmiManager.SensorGpuTempFallback1);
        Assert.Equal(0x0B01u, AcerWmiManager.SensorGpuTempFallback2);
        Assert.Equal(0x0601u, AcerWmiManager.SensorGpuRpm);
    }

    // =========================================================================
    // 3. Background CPU & Process Overhead Invariants
    // =========================================================================

    [Fact]
    public void BackgroundEngine_HysteresisCycles_EnforcesMinimumHoldDelay()
    {
        // Hysteresis requirement: At least 2 cycles (2s per cycle = 4s minimum hold time)
        // before fan speeds step down, preventing thermal flutter and fan revving
        Assert.True(
            TrayApplication.HysteresisHoldCycles >= 2,
            "TrayApplication.HysteresisHoldCycles must be >= 2 to prevent rapid fan revving."
        );
    }

    [Fact]
    public void ZeroProcess_PowerCfgPath_IsAbsoluteAndPointsToSystemDirectory()
    {
        // Guardrail: Never invoke powercfg without full System32 path to prevent PATH hijacking
        string path = CpuPowerManager.CachedPowerCfgPath;

        Assert.True(Path.IsPathRooted(path), "powercfg path must be absolute and rooted.");
        Assert.StartsWith(Environment.SystemDirectory, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("powercfg.exe", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZeroProcess_ArchitectureAudit_NoBackgroundLoopsSpawnConsoleExecutables()
    {
        // Scan the Nitrous application assembly to verify prohibited console executables are never referenced
        var assembly = typeof(UpdateManager).Assembly;
        var prohibitedBinaries = new[] { "nvidia-smi.exe", "wmic.exe" };

        var types = assembly.GetTypes();
        foreach (var type in types)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (f.FieldType == typeof(string) && f.IsLiteral)
                {
                    string? val = f.GetValue(null)?.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        foreach (var prohibited in prohibitedBinaries)
                        {
                            Assert.False(
                                val.Contains(prohibited, StringComparison.OrdinalIgnoreCase),
                                $"Found prohibited console executable reference '{prohibited}' in field {type.Name}.{f.Name}."
                            );
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void IPC_NamedSynchronizationPrimitives_AreConfiguredCorrectly()
    {
        // Single instance mutexes and event primitives for fast in-process threadpool wakeup
        Assert.Equal("Nitrous_SingleInstance_Mutex_Lock", Program.AppMutexName);
        Assert.Equal(@"Local\Nitrous_Dashboard_SingleInstance_Mutex", Program.DashboardMutexName);
        Assert.Equal(@"Local\Nitrous_ShowDashboard_Event", Program.DashboardEventName);

        // Dashboard primitives must use Local\ namespace for session isolation
        Assert.StartsWith(@"Local\", Program.DashboardMutexName);
        Assert.StartsWith(@"Local\", Program.DashboardEventName);
    }
}
