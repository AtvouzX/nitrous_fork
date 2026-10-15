using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Nitrous.Managers;

public partial class NvidiaGpuManager
{
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
