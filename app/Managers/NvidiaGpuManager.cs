using System;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

namespace Nitrous.Managers;

public class NvidiaGpuManager : IDisposable
{
    private PhysicalGPU? _internalGpu;
    public bool IsValid => _internalGpu != null;

    public int MaxCoreOffset = 250;
    public int MinCoreOffset = -250;
    public int MaxMemoryOffset = 1000;
    public int MinMemoryOffset = -1000;

    // Persist the NVML device handle for rapid polling
    private IntPtr _nvmlDeviceHandle = IntPtr.Zero;
    private double _memoryClockDivisor = 4.0; // Default to GDDR5/6/6X
    private string _cachedArchName = "Unknown";

    public NvidiaGpuManager()
    {
        InitializeNvAPI();
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

        try
        {
            try { NVIDIA.Unload(); } catch { }
            try { NativeNvml.Shutdown(); } catch { }

            // Initialize NvAPI for Overclocking
            NVIDIA.Initialize();
            _internalGpu = GetInternalDiscreteGpu();

            // Initialize NVML for Telemetry
            if (NativeNvml.Init() == NvmlReturn.Success)
            {
                // Grab the handle for the primary GPU (index 0)
                NativeNvml.DeviceGetHandleByIndex(0, out _nvmlDeviceHandle);

                // Fetch architecture to set the correct memory divisor for GDDR7 (Blackwell+)
                if (NativeNvml.DeviceGetArchitecture(_nvmlDeviceHandle, out NvmlDeviceArchitecture arch) == NvmlReturn.Success)
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

    public bool GetClocks(out int core, out int memory)
    {
        core = memory = 0;
        if (!IsValid) return false;

        try
        {
            IPerformanceStates20Info states = GPUApi.GetPerformanceStates20(_internalGpu!.Handle);
            var p0Clocks = states.Clocks[PerformanceStateId.P0_3DPerformance];

            var coreClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics);
            var memClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory);

            core = (coreClock?.FrequencyDeltaInkHz.DeltaValue ?? 0) / 1000;
            memory = (memClock?.FrequencyDeltaInkHz.DeltaValue ?? 0) / 1000;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public int SetClocks(int core, int memory) => SetClocksInternal(core, memory);

    private int SetClocksInternal(int core, int memory)
    {
        if (!IsValid) return 0;

        if (core < MinCoreOffset || core > MaxCoreOffset) return 0;
        if (memory < MinMemoryOffset || memory > MaxMemoryOffset) return 0;

        GetClocks(out int currentCore, out int currentMemory);

        if (Math.Abs(core - currentCore) < 5 && Math.Abs(memory - currentMemory) < 5)
            return 1;

        var coreClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(core * 1000));
        var memoryClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memory * 1000));

        PerformanceStates20ClockEntryV1[] clocks = { coreClock, memoryClock };
        PerformanceStates20BaseVoltageEntryV1[] voltages = { };

        PerformanceStates20InfoV1.PerformanceState20[] performanceStates = {
                new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clocks, voltages)
            };

        var overclock = new PerformanceStates20InfoV1(performanceStates, 2, 0);

        try
        {
            GPUApi.SetPerformanceStates20(_internalGpu!.Handle, overclock);
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    public async Task ApplyOnBootAsync(PowerProfile currentProfile)
    {
        int core = SettingsManager.Get($"GpuCore_{currentProfile}", GetDefaultCore(currentProfile));
        int memory = SettingsManager.Get($"GpuMemory_{currentProfile}", GetDefaultMemory(currentProfile));

        if (core == 0 && memory == 0) return;

        const int maxAttempts = 8;
        const int delayMs = 1500;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (_internalGpu == null) InitializeNvAPI();

                if (IsValid)
                {
                    int result = SetClocksInternal(core, memory);
                    if (result == 1) return;
                }
            }
            catch { }

            await Task.Delay(delayMs);
        }
    }

    public async Task<int> ApplyPowerProfileOcAsync(PowerProfile profile)
    {
        return await Task.Run(() =>
        {
            int coreOffset = SettingsManager.Get($"GpuCore_{profile}", GetDefaultCore(profile));
            int memoryOffset = SettingsManager.Get($"GpuMemory_{profile}", GetDefaultMemory(profile));

            return SetClocksInternal(coreOffset, memoryOffset);
        });
    }

    // Helpers to track default values per profile
    public int GetDefaultCore(PowerProfile profile) => profile switch
    {
        PowerProfile.Quiet => -100,
        PowerProfile.Performance => 100,
        PowerProfile.Turbo => 150,
        _ => 0
    };

    public int GetDefaultMemory(PowerProfile profile) => profile switch
    {
        PowerProfile.Quiet => -200,
        PowerProfile.Performance => 150,
        PowerProfile.Turbo => 300,
        _ => 0
    };

    // Saves the user's custom slider values to the active profile
    public void SaveCustomProfileOc(PowerProfile profile, int core, int memory)
    {
        SettingsManager.Save($"GpuCore_{profile}", core);
        SettingsManager.Save($"GpuMemory_{profile}", memory);
    }

    // Overwrites the custom save with defaults
    public async Task ResetProfileToDefaultsAsync(PowerProfile profile)
    {
        SettingsManager.Save($"GpuCore_{profile}", GetDefaultCore(profile));
        SettingsManager.Save($"GpuMemory_{profile}", GetDefaultMemory(profile));
        await ApplyPowerProfileOcAsync(profile);
    }

    public void Dispose()
    {
        try
        {
            NVIDIA.Unload();
            NativeNvml.Shutdown();
        }
        catch { }
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
            int cr = NativeCM.CM_Locate_DevNode(out uint devInst, _nvidiaInstanceId, NativeCM.CM_LOCATE_DEVNODE_NORMAL);
            if (cr != NativeCM.CR_SUCCESS)
                return false;

            // Read the CM_POWER_DATA structure from DEVPKEY_Device_PowerData
            // The PD_MostRecentPowerState field tells us the actual D-state.
            var powerData = new NativeCM.CM_POWER_DATA();
            uint dataSize = (uint)Marshal.SizeOf<NativeCM.CM_POWER_DATA>();
            uint propType = 0;

            cr = NativeCM.CM_Get_DevNode_Property(
                devInst,
                ref NativeCM.DEVPKEY_Device_PowerData,
                out propType,
                ref powerData,
                ref dataSize,
                0);

            if (cr != NativeCM.CR_SUCCESS)
                return false;

            // PowerDeviceD0 = 1 (fully powered / active)
            // PowerDeviceD1/D2/D3 = 2/3/4 (various sleep states including D3Cold)
            return powerData.PD_MostRecentPowerState == NativeCM.PowerDeviceD0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NvidiaGpuManager] IsGpuAwake CM API failed: {ex.Message}");
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

            int cr = NativeCM.CM_Get_Device_ID_List_Size(out uint listSize, filter,
                NativeCM.CM_GETIDLIST_FILTER_CLASS | NativeCM.CM_GETIDLIST_FILTER_PRESENT);
            if (cr != NativeCM.CR_SUCCESS || listSize == 0)
                return null;

            char[] buffer = new char[listSize];
            cr = NativeCM.CM_Get_Device_ID_List(filter, buffer, listSize,
                NativeCM.CM_GETIDLIST_FILTER_CLASS | NativeCM.CM_GETIDLIST_FILTER_PRESENT);
            if (cr != NativeCM.CR_SUCCESS)
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
            Debug.WriteLine($"[NvidiaGpuManager] ResolveNvidiaInstanceId failed: {ex.Message}");
            return null;
        }
    }

    public async Task<GpuTelemetry> GetNvmlTelemetryAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var t = new GpuTelemetry { Architecture = _cachedArchName };

            // Phase 1: Native Sleep Check wrapper
            // If the GPU is asleep, bypass NVML completely to preserve D3Cold power state.
            if (!IsGpuAwake())
            {
                t.PState = "Sleep";
                t.CoreTemp = 0;
                t.GpuLoad = 0;
                t.Name = "NVIDIA (Asleep)";
                return t;
            }

            // If the application started while the GPU was asleep, the NVML handle might be zero.
            // Now that we know it is awake, initialize the handle if it's missing.
            if (_nvmlDeviceHandle == IntPtr.Zero)
            {
                if (NativeNvml.Init() == NvmlReturn.Success)
                {
                    NativeNvml.DeviceGetHandleByIndex(0, out _nvmlDeviceHandle);
                    if (NativeNvml.DeviceGetArchitecture(_nvmlDeviceHandle, out NvmlDeviceArchitecture arch) == NvmlReturn.Success)
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
                // Phase 2: Check if Nitrous itself is the only process keeping the GPU awake.
                // If so, shut down NVML and NvAPI to allow the GPU to go back to D3Cold sleep.
                uint graphicsCount = 32;
                var graphicsInfos = new NvmlProcessInfo[graphicsCount];
                bool hasGraphics = NativeNvml.DeviceGetGraphicsRunningProcesses(_nvmlDeviceHandle, ref graphicsCount, graphicsInfos) == NvmlReturn.Success;

                uint computeCount = 32;
                var computeInfos = new NvmlProcessInfo[computeCount];
                bool hasCompute = NativeNvml.DeviceGetComputeRunningProcesses(_nvmlDeviceHandle, ref computeCount, computeInfos) == NvmlReturn.Success;

                if (hasGraphics || hasCompute)
                {
                    int currentPid = Environment.ProcessId;
                    bool onlyNitrous = true;
                    bool hasAnyProcess = false;

                    if (hasGraphics)
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

                    if (hasCompute && onlyNitrous)
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

                    if (hasAnyProcess && onlyNitrous)
                    {
                        Debug.WriteLine("[NvidiaGpuManager] Only Nitrous is running on the GPU. Unloading NVML to allow sleep.");
                        try { NVIDIA.Unload(); } catch { }
                        try { NativeNvml.Shutdown(); } catch { }
                        
                        _nvmlDeviceHandle = IntPtr.Zero;
                        _internalGpu = null;

                        t.PState = "Sleep";
                        t.CoreTemp = 0;
                        t.GpuLoad = 0;
                        t.Name = "NVIDIA (Asleep)";
                        return t;
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
                var nameBuilder = new StringBuilder(64);
                if (NativeNvml.DeviceGetName(_nvmlDeviceHandle, nameBuilder, (uint)nameBuilder.Capacity) == NvmlReturn.Success)
                    t.Name = nameBuilder.ToString();

                // Load
                if (NativeNvml.DeviceGetUtilizationRates(_nvmlDeviceHandle, out NvmlUtilization util) == NvmlReturn.Success)
                    t.GpuLoad = (int)util.Gpu;

                // Temp
                if (NativeNvml.DeviceGetTemperature(_nvmlDeviceHandle, NvmlTemperatureSensors.Gpu, out uint temp) == NvmlReturn.Success)
                    t.CoreTemp = (int)temp;

                // Memory
                if (NativeNvml.DeviceGetMemoryInfo(_nvmlDeviceHandle, out NvmlMemory mem) == NvmlReturn.Success)
                {
                    // Convert bytes to Megabytes
                    t.VramUsedMb = (int)(mem.Used / (1024 * 1024));
                    t.VramTotalMb = (int)(mem.Total / (1024 * 1024));
                }

                // P-State (Format string to match SMI output like "P0", "P8")
                if (NativeNvml.DeviceGetPerformanceState(_nvmlDeviceHandle, out NvmlPstates pState) == NvmlReturn.Success)
                    t.PState = pState.ToString().Replace("Pstate", "P");

                // Clocks
                if (NativeNvml.DeviceGetClockInfo(_nvmlDeviceHandle, NvmlClockType.Graphics, out uint coreClock) == NvmlReturn.Success)
                    t.CurrentCoreClock = (int)coreClock;

                if (NativeNvml.DeviceGetClockInfo(_nvmlDeviceHandle, NvmlClockType.Mem, out uint memClock) == NvmlReturn.Success)
                    t.CurrentMemoryClock = (int)(memClock / _memoryClockDivisor);

                // Power Limits (Convert milliwatts to Watts)
                if (NativeNvml.DeviceGetPowerUsage(_nvmlDeviceHandle, out uint powerDraw) == NvmlReturn.Success)
                    t.PowerDrawW = Math.Round(powerDraw / 1000.0, 1);

                if (NativeNvml.DeviceGetEnforcedPowerLimit(_nvmlDeviceHandle, out uint enforced) == NvmlReturn.Success)
                    t.EnforcedPowerLimitW = Math.Round(enforced / 1000.0, 1);

                if (NativeNvml.DeviceGetPowerManagementLimitConstraints(_nvmlDeviceHandle, out uint minLimit, out uint maxLimit) == NvmlReturn.Success)
                {
                    t.MinPowerLimitW = Math.Round(minLimit / 1000.0, 1);
                    t.MaxPowerLimitW = Math.Round(maxLimit / 1000.0, 1);
                }
            }
            catch
            {
                // Ignore P/Invoke exceptions on unsupported platforms or sleeping GPUs
            }

            return t;
        }, cancellationToken);
    }

    #region NVML Native Bindings

    public enum NvmlReturn
    {
        Success = 0,
        Uninitialized = 1,
        InvalidArgument = 2,
        NotSupported = 3,
        NoPermission = 4,
        AlreadyInitialized = 5,
        NotFound = 6,
        InsufficientSize = 7,
        InsufficientPower = 8,
        DriverNotLoaded = 9,
        Timeout = 10,
        IrqIssue = 11,
        LibraryNotFound = 12,
        FunctionNotFound = 13,
        CorruptedInforom = 14,
        GpuIsLost = 15,
        ResetRequired = 16,
        OperatingSystem = 17,
        LibRmVersionMismatch = 18,
        InUse = 19,
        Memory = 20,
        NoData = 21,
        VgpuEccNotSupported = 22,
        InsufficientResources = 23,
        Unknown = 999
    }

    public enum NvmlDeviceArchitecture
    {
        Kepler = 2, Maxwell = 3, Pascal = 4, Volta = 5,
        Turing = 6, Ampere = 7, Ada = 8, Hopper = 9,
        Blackwell = 10, Rubin = 13
    }

    public enum NvmlClockType { Graphics = 0, Sm = 1, Mem = 2, Video = 3 }
    public enum NvmlTemperatureSensors { Gpu = 0 }
    public enum NvmlPstates
    {
        Pstate0 = 0, Pstate1 = 1, Pstate2 = 2, Pstate3 = 3,
        Pstate4 = 4, Pstate5 = 5, Pstate6 = 6, Pstate7 = 7,
        Pstate8 = 8, Pstate9 = 9, Pstate10 = 10, Pstate11 = 11,
        Pstate12 = 12, Pstate13 = 13, Pstate14 = 14, Pstate15 = 15,
        Unknown = 32
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlProcessInfo
    {
        public uint Pid;
        public ulong UsedGpuMemory;
    }

    private static class NativeNvml
    {
        private const string NvmlDll = "nvml.dll";

        [DllImport(NvmlDll, EntryPoint = "nvmlInit_v2")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn Init();

        [DllImport(NvmlDll, EntryPoint = "nvmlShutdown")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn Shutdown();

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetName")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetName(IntPtr device, StringBuilder name, uint length);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetUtilizationRates")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetTemperature")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetTemperature(IntPtr device, NvmlTemperatureSensors sensorType, out uint temp);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetClockInfo")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetClockInfo(IntPtr device, NvmlClockType type, out uint clock);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetMemoryInfo")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerUsage")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetPowerUsage(IntPtr device, out uint power);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetEnforcedPowerLimit(IntPtr device, out uint limit);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerManagementLimitConstraints")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetPowerManagementLimitConstraints(IntPtr device, out uint minLimit, out uint maxLimit);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPerformanceState")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetPerformanceState(IntPtr device, out NvmlPstates pState);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetArchitecture")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetArchitecture(IntPtr device, out NvmlDeviceArchitecture arch);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetGraphicsRunningProcesses")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetGraphicsRunningProcesses(IntPtr device, ref uint infoCount, [Out] NvmlProcessInfo[] infos);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetComputeRunningProcesses")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern NvmlReturn DeviceGetComputeRunningProcesses(IntPtr device, ref uint infoCount, [Out] NvmlProcessInfo[] infos);
    }
    #endregion

    #region Windows Configuration Manager (CM) Native Bindings

    /// <summary>
    /// P/Invoke bindings for the Windows Configuration Manager API (cfgmgr32.dll).
    /// Used to read the GPU's actual device power state (D0/D3) without touching the GPU driver.
    /// </summary>
    private static class NativeCM
    {
        public const int CR_SUCCESS = 0;
        public const uint CM_LOCATE_DEVNODE_NORMAL = 0;
        public const uint CM_GETIDLIST_FILTER_CLASS = 0x00000200;
        public const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;

        // DEVICE_POWER_STATE values
        public const int PowerDeviceD0 = 1; // Fully powered
        // PowerDeviceD1 = 2, PowerDeviceD2 = 3, PowerDeviceD3 = 4 (all sleep states)

        // DEVPKEY_Device_PowerData: {a45c254e-df1c-4efd-8020-67d146a850e0}, 32
        public static NativeCM.DEVPROPKEY DEVPKEY_Device_PowerData = new NativeCM.DEVPROPKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 32
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        /// <summary>
        /// Matches the native CM_POWER_DATA structure.
        /// We only need PD_MostRecentPowerState (offset 4).
        /// https://learn.microsoft.com/en-us/windows/win32/power/cm-power-data
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CM_POWER_DATA
        {
            public uint PD_Size;
            public int PD_MostRecentPowerState; // DEVICE_POWER_STATE enum
            public uint PD_Capabilities;
            public uint PD_D1Latency;
            public uint PD_D2Latency;
            public uint PD_D3Latency;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public int[] PD_PowerStateMapping; // DEVICE_POWER_STATE[POWER_SYSTEM_MAXIMUM]
            public int PD_DeepestSystemWake; // SYSTEM_POWER_STATE
        }

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Locate_DevNode(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Get_Device_ID_List_Size(out uint pulLen, string pszFilter, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Get_Device_ID_List(string pszFilter, [Out] char[] Buffer, uint BufferLen, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Get_DevNode_Property(
            uint devInst,
            ref DEVPROPKEY propertyKey,
            out uint propertyType,
            ref CM_POWER_DATA propertyBuffer,
            ref uint propertyBufferSize,
            uint ulFlags);
    }
    #endregion
}
