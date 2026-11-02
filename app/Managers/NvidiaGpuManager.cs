using System;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Interop;

namespace Nitrous.Managers;

public partial class NvidiaGpuManager : IDisposable
{
    private PhysicalGPU? _internalGpu;
    private int _isAutoApplyingClocks;
    public bool IsValid => _internalGpu != null || (IsGpuAwake() && EnsureInitialized());

    public const int GpuSleepHysteresisCycles = 4; // 4 cycles * 1.5s dashboard / 2s tray = 6s / 8s hold

    public void ResetGpuHandle()
    {
        _internalGpu = null;
        _nvmlDeviceHandle = IntPtr.Zero;
        _lastAutoApplyTick = 0;
        try
        {
            NVIDIA.Unload();
        }
        catch
        {
        }

        try
        {
            NativeNvml.Shutdown();
        }
        catch
        {
        }
    }

    /// <summary>
    /// Pure state evaluator for GPU sleep hysteresis.
    /// <summary>
    /// Symmetrically absorbs transient 1-2s RTD3 drops, CM API read flickers, and transient wake blips
    /// by requiring <paramref name="holdCycles"/> consecutive readings before confirming transition
    /// to sleep (resetting handles) or awake (auto-applying clocks).
    /// </summary>
    public static (bool newWasGpuAwake, int newHoldTicks, bool shouldAutoApply, bool shouldResetHandle)
        EvaluateGpuSleepHysteresis(
            bool wasGpuAwake,
            bool isGpuAwake,
            int currentHoldTicks,
            int holdCycles = GpuSleepHysteresisCycles)
    {
        if (isGpuAwake)
        {
            if (wasGpuAwake)
            {
                // Already confirmed awake and still awake
                return (true, 0, false, false);
            }

            int nextHold = currentHoldTicks + 1;
            if (nextHold >= holdCycles)
            {
                // Confirmed sustained wake after hold cycles
                return (true, 0, true, false);
            }

            // Holding in sleep until sustained wake is confirmed
            return (false, nextHold, false, false);
        }
        else
        {
            if (wasGpuAwake)
            {
                int nextHold = currentHoldTicks + 1;
                if (nextHold >= holdCycles)
                {
                    // Confirmed sustained sleep after hold cycles
                    return (false, 0, false, true);
                }

                // Holding in awake until sustained sleep is confirmed
                return (true, nextHold, false, false);
            }

            // Already confirmed asleep and still asleep
            return (false, 0, false, false);
        }
    }

    public bool EnsureInitialized()
    {
        if (!IsGpuAwake())
        {
            if (_internalGpu != null)
            {
                ResetGpuHandle();
            }

            return false;
        }

        if (_internalGpu != null) return true;
        InitializeNvAPI();
        return _internalGpu != null;
    }

    public const int MaxCoreOffset = 200;
    public const int MinCoreOffset = -200;
    public const int MaxMemoryOffset = 500;
    public const int MinMemoryOffset = -500;

    public int ClampCoreOffset(int offset) => Math.Clamp(offset, MinCoreOffset, MaxCoreOffset);

    public int ClampMemoryOffset(int offset) => Math.Clamp(offset, MinMemoryOffset, MaxMemoryOffset);

    // Persist the NVML device handle for rapid polling
    private IntPtr _nvmlDeviceHandle = IntPtr.Zero;
    private double _memoryClockDivisor = 4.0; // Default to GDDR5/6/6X
    private string _cachedArchName = "Unknown";
    private string? _cachedGpuName;
    private int _cachedVramTotal;
    private NvmlProcessInfo[]? _cachedGraphicsInfos;
    private NvmlProcessInfo[]? _cachedComputeInfos;
    private DateTime _sleepPendingUntil = DateTime.MinValue;

    private static string FormatPState(NvmlPstates pState)
    {
        return pState switch
        {
            NvmlPstates.Pstate0 => "P0",
            NvmlPstates.Pstate1 => "P1",
            NvmlPstates.Pstate2 => "P2",
            NvmlPstates.Pstate3 => "P3",
            NvmlPstates.Pstate4 => "P4",
            NvmlPstates.Pstate5 => "P5",
            NvmlPstates.Pstate6 => "P6",
            NvmlPstates.Pstate7 => "P7",
            NvmlPstates.Pstate8 => "P8",
            NvmlPstates.Pstate9 => "P9",
            NvmlPstates.Pstate10 => "P10",
            NvmlPstates.Pstate11 => "P11",
            NvmlPstates.Pstate12 => "P12",
            NvmlPstates.Pstate13 => "P13",
            NvmlPstates.Pstate14 => "P14",
            NvmlPstates.Pstate15 => "P15",
            _ => "Unknown"
        };
    }

    public NvidiaGpuManager()
    {
        MigrateLegacyDefaultOffsets();
        InitializeNvAPI();
    }

    private static void MigrateLegacyDefaultOffsets()
    {
        try
        {
            if (SettingsManager.Get("GpuOffsetsMigratedToStock_v1", 0) == 1) return;

            var profiles = new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo };
            foreach (var p in profiles)
            {
                int c = SettingsManager.Get($"GpuCore_{p}", 0);
                int m = SettingsManager.Get($"GpuMemory_{p}", 0);

                if ((c == 50 && m == 100) || (c == -100 && m == -200) || (c == 100 && m == 150) || (c == 150 && m == 300))
                {
                    SettingsManager.Save($"GpuCore_{p}", 0);
                    SettingsManager.Save($"GpuMemory_{p}", 0);
                    LogManager.Info($"Migrated legacy GPU offsets for {p} to stock (0/0 MHz)", "NvidiaGpu");
                }
            }

            SettingsManager.Save("GpuOffsetsMigratedToStock_v1", 1);
        }
        catch (Exception ex)
        {
            LogManager.Warn($"MigrateLegacyDefaultOffsets failed: {ex.Message}", "NvidiaGpu");
        }
    }

    private void InitializeNvAPI()
    {
        // Guard against binding unmanaged GPU driver handles in test host environments
        string procName = Process.GetCurrentProcess().ProcessName;
        if (procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
            procName.Contains("vstest", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Guard against waking sleeping dGPU in D3Cold sleep
        if (!IsGpuAwake())
        {
            return;
        }

        try
        {
            try
            {
                NVIDIA.Unload();
            }
            catch
            {
            }

            try
            {
                NativeNvml.Shutdown();
            }
            catch
            {
            }

            // Initialize NvAPI for Overclocking
            NVIDIA.Initialize();
            _internalGpu = GetInternalDiscreteGpu();

            // Initialize NVML for Telemetry
            if (NativeNvml.Init() == NvmlReturn.Success)
            {
                // Grab the handle for the primary GPU (index 0).
                // Must check return: failed out-param may hold garbage and AV native calls.
                if (NativeNvml.DeviceGetHandleByIndex(0, out _nvmlDeviceHandle) != NvmlReturn.Success)
                    _nvmlDeviceHandle = IntPtr.Zero;

                // Fetch architecture to set the correct memory divisor for GDDR7 (Blackwell+)
                if (NativeNvml.DeviceGetArchitecture(_nvmlDeviceHandle, out NvmlDeviceArchitecture arch) ==
                    NvmlReturn.Success)
                {
                    _memoryClockDivisor = ((int)arch >= 10) ? 8.0 : 4.0;
                    _cachedArchName = arch.ToString();
                }
            }
        }
        catch
        {
            _internalGpu = null;
            _nvmlDeviceHandle = IntPtr.Zero;
        }
    }

    private static PhysicalGPU? GetInternalDiscreteGpu()
    {
        try
        {
            return PhysicalGPU
                       .GetPhysicalGPUs()
                       .FirstOrDefault(gpu => gpu.SystemType == SystemType.Laptop)
                   ?? PhysicalGPU.GetPhysicalGPUs().FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }


    public static bool IsAcPowerConnected()
    {
        try
        {
            return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                   System.Windows.Forms.PowerLineStatus.Online;
        }
        catch
        {
            return true;
        }
    }


    public void Dispose()
    {
        try
        {
            ResetGpuHandle();
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Error during NvidiaGpuManager disposal: {ex.Message}", "NvidiaGpu");
        }
    }

    public class GpuTelemetry
    {
        public string Name { get; set; } = "NVIDIA GPU";
        public int CoreTemp { get; set; }
        public int GpuLoad { get; set; }
        public int VramUsedMb { get; set; }
        public int VramTotalMb { get; set; }
        public string PState { get; set; } = "Unknown";
        public int CurrentCoreClock { get; set; }
        public int CurrentMemoryClock { get; set; }
        public double PowerDrawW { get; set; }
        public double MinPowerLimitW { get; set; }
        public double MaxPowerLimitW { get; set; }
        public double EnforcedPowerLimitW { get; set; }
        public string Architecture { get; set; } = "Unknown";
        /// <summary>
        /// True only when the utilization query succeeded. Partial or failed NVML reads
        /// leave this false so the UI can hold the last good sample instead of drawing 0%.
        /// </summary>
        public bool HasLoadSample { get; set; }

        /// <summary>
        /// True when this object carries a live sample the GPU page may render.
        /// Failed reads and placeholder names stay hidden until a real query succeeds.
        /// </summary>
        public bool IsDisplayable =>
            HasLoadSample && !string.IsNullOrEmpty(Name) && Name != "Unknown";
    }

    // Cached PNP instance ID for the NVIDIA dGPU, resolved once on first call.
    private static string? _nvidiaInstanceId;
    private static bool _instanceIdResolved;

    /// <summary>
    /// Determines if the discrete NVIDIA GPU is powered on (D0) by reading its
    /// device power state from the Windows Configuration Manager API.
    /// This reads from Windows' own records — it never touches the GPU driver,
    /// so it cannot wake the GPU from D3Cold sleep.
    /// Matches the technique used by OpenSense (GpuPowerState.cs) and the
    /// NVIDIA GPU Activity tray icon.
    /// </summary>
    public static bool IsGpuAwake()
    {
        try
        {
            // Resolve the PNP instance ID once
            if (!_instanceIdResolved)
            {
                _nvidiaInstanceId = ResolveNvidiaInstanceId();
                _instanceIdResolved = true;
            }

            if (_nvidiaInstanceId == null)
                return false;

            // Locate the device node by its PNP instance ID
            int cr = Cfgmgr32.CM_Locate_DevNode(out uint devInst, _nvidiaInstanceId, Cfgmgr32.CM_LOCATE_DEVNODE_NORMAL);
            if (cr != Cfgmgr32.CR_SUCCESS)
                return false;

            // Read the CM_POWER_DATA structure from DEVPKEY_Device_PowerData
            // The PD_MostRecentPowerState field tells us the actual D-state.
            var powerData = new Cfgmgr32.CM_POWER_DATA();
            uint dataSize = (uint)Marshal.SizeOf<Cfgmgr32.CM_POWER_DATA>();
            uint propType = 0;

            var powerKey = Cfgmgr32.DEVPKEY_Device_PowerData;
            cr = Cfgmgr32.CM_Get_DevNode_Property(
                devInst,
                ref powerKey,
                out propType,
                ref powerData,
                ref dataSize,
                0);

            if (cr != Cfgmgr32.CR_SUCCESS)
                return false;

            // PowerDeviceD0 = 1 (fully powered / active)
            // PowerDeviceD1/D2/D3 = 2/3/4 (various sleep states including D3Cold)
            return powerData.PD_MostRecentPowerState == Cfgmgr32.PowerDeviceD0;
        }
        catch (Exception ex)
        {
            LogManager.Warn($"IsGpuAwake CM API failed: {ex.Message}", "NvidiaGpu");
        }

        // Ultimate fallback — assume awake so telemetry is never permanently blocked
        return true;
    }

    /// <summary>
    /// Finds the PNP instance ID of the first NVIDIA PCI display adapter.
    /// Uses CM_Get_Device_ID_List filtered to the Display device class.
    /// </summary>
    private static string? ResolveNvidiaInstanceId()
    {
        try
        {
            // GUID_DEVCLASS_DISPLAY = {4D36E968-E325-11CE-BFC1-08002BE10318}
            string filter = "{4D36E968-E325-11CE-BFC1-08002BE10318}";

            int cr = Cfgmgr32.CM_Get_Device_ID_List_Size(out uint listSize, filter,
                Cfgmgr32.CM_GETIDLIST_FILTER_CLASS | Cfgmgr32.CM_GETIDLIST_FILTER_PRESENT);
            if (cr != Cfgmgr32.CR_SUCCESS || listSize == 0)
                return null;

            char[] buffer = new char[listSize];
            cr = Cfgmgr32.CM_Get_Device_ID_List(filter, buffer, listSize,
                Cfgmgr32.CM_GETIDLIST_FILTER_CLASS | Cfgmgr32.CM_GETIDLIST_FILTER_PRESENT);
            if (cr != Cfgmgr32.CR_SUCCESS)
                return null;

            // The buffer is a multi-string (null-separated, double-null terminated)
            string allIds = new string(buffer);
            string[] ids = allIds.Split('\0', StringSplitOptions.RemoveEmptyEntries);

            // Find the NVIDIA GPU by its PCI vendor ID (VEN_10DE)
            return ids.FirstOrDefault(id =>
                id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            LogManager.Warn($"ResolveNvidiaInstanceId failed: {ex.Message}", "NvidiaGpu");
            return null;
        }
    }

    public static bool IsTestHost()
    {
        string procName = Process.GetCurrentProcess().ProcessName;
        return procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
               procName.Contains("vstest", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<GpuTelemetry> GetNvmlTelemetryAsync(
        CancellationToken cancellationToken = default,
        bool? isAwake = null)
    {
        return await Task.Run(() =>
        {
            var t = new GpuTelemetry { Architecture = _cachedArchName };

            // Never touch live NVML from the test host: parallel tests racing
            // Init/Shutdown/handle reads AV the process (FailFast, uncatchable).
            if (IsTestHost())
            {
                t.PState = "Sleep";
                t.CoreTemp = 0;
                t.GpuLoad = 0;
                t.Name = "NVIDIA (Asleep)";
                return t;
            }

            // Phase 1: Native Sleep Check wrapper
            // If the GPU is asleep, bypass NVML completely to preserve D3Cold power state.
            // Caller may pass its own sample so a second CM read cannot disagree mid-transition.
            if (!(isAwake ?? IsGpuAwake()))
            {
                _sleepPendingUntil = DateTime.MinValue;
                t.PState = "Sleep";
                t.CoreTemp = 0;
                t.GpuLoad = 0;
                t.Name = "NVIDIA (Asleep)";
                return t;
            }

            // If on battery and we recently unloaded NVML to allow D3Cold sleep, do not re-init NVML immediately
            // while the driver is transitioning to sleep.
            bool isAcOnline = IsAcPowerConnected();
            if (!isAcOnline && DateTime.UtcNow < _sleepPendingUntil)
            {
                t.PState = "Sleep";
                t.CoreTemp = 0;
                t.GpuLoad = 0;
                t.Name = "NVIDIA (Asleep)";
                return t;
            }

            if (isAcOnline)
            {
                _sleepPendingUntil = DateTime.MinValue;
            }

            // If the application started while the GPU was asleep, the NVML handle might be zero.
            // Now that we know it is awake, initialize the handle if it's missing.
            if (_nvmlDeviceHandle == IntPtr.Zero)
            {
                if (NativeNvml.Init() == NvmlReturn.Success)
                {
                    if (NativeNvml.DeviceGetHandleByIndex(0, out _nvmlDeviceHandle) != NvmlReturn.Success)
                        _nvmlDeviceHandle = IntPtr.Zero;
                    if (NativeNvml.DeviceGetArchitecture(_nvmlDeviceHandle, out NvmlDeviceArchitecture arch) ==
                        NvmlReturn.Success)
                    {
                        _memoryClockDivisor = ((int)arch >= 10) ? 8.0 : 4.0;
                        _cachedArchName = arch.ToString();
                        t.Architecture = _cachedArchName;
                    }
                }
            }

            if (_nvmlDeviceHandle == IntPtr.Zero) return t;

            try
            {
                // Phase 2: Check if Nitrous itself is the only process keeping the GPU awake on battery.
                // If so, shut down NVML and NvAPI to allow the GPU to go back to D3Cold sleep.
                // On AC power, the discrete GPU remains active and is never forcibly put to sleep while telemetry is running.
                if (!isAcOnline)
                {
                    uint graphicsCount = 32;
                    _cachedGraphicsInfos ??= new NvmlProcessInfo[32];
                    bool hasGraphics =
                        NativeNvml.DeviceGetGraphicsRunningProcesses(_nvmlDeviceHandle, ref graphicsCount,
                            _cachedGraphicsInfos) == NvmlReturn.Success;

                    uint computeCount = 32;
                    _cachedComputeInfos ??= new NvmlProcessInfo[32];
                    bool hasCompute =
                        NativeNvml.DeviceGetComputeRunningProcesses(_nvmlDeviceHandle, ref computeCount,
                            _cachedComputeInfos) == NvmlReturn.Success;

                    if (hasGraphics || hasCompute)
                    {
                        bool shouldSleep = IsOnlyNitrousRunningOnGpu(
                            hasGraphics ? _cachedGraphicsInfos : null, hasGraphics ? graphicsCount : 0,
                            hasCompute ? _cachedComputeInfos : null, hasCompute ? computeCount : 0,
                            Environment.ProcessId);

                        if (shouldSleep)
                        {
                            LogManager.Info(
                                "Only Nitrous is running on the GPU on battery. Unloading NVML to allow sleep.",
                                "NvidiaGpu");
                            try
                            {
                                NVIDIA.Unload();
                            }
                            catch
                            {
                            }

                            try
                            {
                                NativeNvml.Shutdown();
                            }
                            catch
                            {
                            }

                            _nvmlDeviceHandle = IntPtr.Zero;
                            _internalGpu = null;
                            _sleepPendingUntil = DateTime.UtcNow.AddSeconds(15);

                            t.PState = "Sleep";
                            t.CoreTemp = 0;
                            t.GpuLoad = 0;
                            t.Name = "NVIDIA (Asleep)";
                            return t;
                        }
                    }
                }
            }
            catch
            {
                // Ignore process check errors on older drivers
            }

            try
            {
                // Name
                if (_cachedGpuName == null)
                {
                    var nameBuilder = new StringBuilder(64);
                    if (NativeNvml.DeviceGetName(_nvmlDeviceHandle, nameBuilder, (uint)nameBuilder.Capacity) ==
                        NvmlReturn.Success)
                        _cachedGpuName = nameBuilder.ToString();
                }

                t.Name = _cachedGpuName ?? "NVIDIA GPU";

                // Load
                if (NativeNvml.DeviceGetUtilizationRates(_nvmlDeviceHandle, out NvmlUtilization util) ==
                    NvmlReturn.Success)
                {
                    t.GpuLoad = (int)util.Gpu;
                    t.HasLoadSample = true;
                }

                // Temp
                if (NativeNvml.DeviceGetTemperature(_nvmlDeviceHandle, NvmlTemperatureSensors.Gpu, out uint temp) ==
                    NvmlReturn.Success)
                    t.CoreTemp = (int)temp;

                // Memory
                if (NativeNvml.DeviceGetMemoryInfo(_nvmlDeviceHandle, out NvmlMemory mem) == NvmlReturn.Success)
                {
                    // Convert bytes to Megabytes
                    t.VramUsedMb = (int)(mem.Used / (1024 * 1024));
                    if (_cachedVramTotal == 0)
                        _cachedVramTotal = (int)(mem.Total / (1024 * 1024));
                    t.VramTotalMb = _cachedVramTotal;
                }

                // P-State (Format string to match SMI output like "P0", "P8")
                if (NativeNvml.DeviceGetPerformanceState(_nvmlDeviceHandle, out NvmlPstates pState) ==
                    NvmlReturn.Success)
                    t.PState = FormatPState(pState);

                // Clocks
                if (NativeNvml.DeviceGetClockInfo(_nvmlDeviceHandle, NvmlClockType.Graphics, out uint coreClock) ==
                    NvmlReturn.Success)
                    t.CurrentCoreClock = (int)coreClock;

                if (NativeNvml.DeviceGetClockInfo(_nvmlDeviceHandle, NvmlClockType.Mem, out uint memClock) ==
                    NvmlReturn.Success)
                    t.CurrentMemoryClock = (int)(memClock / _memoryClockDivisor);

                // Power Limits (Convert milliwatts to Watts)
                if (NativeNvml.DeviceGetPowerUsage(_nvmlDeviceHandle, out uint powerDraw) == NvmlReturn.Success)
                    t.PowerDrawW = UnitConverter.MwToWRounded(powerDraw);

                if (NativeNvml.DeviceGetEnforcedPowerLimit(_nvmlDeviceHandle, out uint enforced) == NvmlReturn.Success)
                    t.EnforcedPowerLimitW = UnitConverter.MwToWRounded(enforced);

                if (NativeNvml.DeviceGetPowerManagementLimitConstraints(_nvmlDeviceHandle, out uint minLimit,
                        out uint maxLimit) == NvmlReturn.Success)
                {
                    t.MinPowerLimitW = UnitConverter.MwToWRounded(minLimit);
                    t.MaxPowerLimitW = UnitConverter.MwToWRounded(maxLimit);
                }
            }
            catch (Exception ex)
            {
                // Ignore P/Invoke exceptions on unsupported platforms or sleeping GPUs
                LogManager.Debug($"GPU telemetry read failed: {ex.Message}", "NvidiaGpu");
            }

            return t;
        }, cancellationToken);
    }

    /// <summary>
    /// Pure function to evaluate if Nitrous is the solitary process keeping the GPU awake.
    /// Extracted for unit testability without relying on NVML bindings.
    /// </summary>
    public static bool IsOnlyNitrousRunningOnGpu(
        NvmlProcessInfo[]? graphicsInfos, uint graphicsCount,
        NvmlProcessInfo[]? computeInfos, uint computeCount,
        int currentPid)
    {
        bool onlyNitrous = true;
        bool hasAnyProcess = false;

        if (graphicsInfos != null && graphicsCount > 0)
        {
            for (int i = 0; i < graphicsCount; i++)
            {
                hasAnyProcess = true;
                if (graphicsInfos[i].Pid != currentPid)
                {
                    onlyNitrous = false;
                    break;
                }
            }
        }

        if (computeInfos != null && computeCount > 0 && onlyNitrous)
        {
            for (int i = 0; i < computeCount; i++)
            {
                hasAnyProcess = true;
                if (computeInfos[i].Pid != currentPid)
                {
                    onlyNitrous = false;
                    break;
                }
            }
        }

        return hasAnyProcess && onlyNitrous;
    }
}
