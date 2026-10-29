using System;
using System.Runtime.InteropServices;

namespace Nitrous.Interop;

/// <summary>
/// P/Invoke bindings for the Windows Configuration Manager API (cfgmgr32.dll).
/// Used to read hardware device node power states (D0-D3) without touching GPU driver APIs.
/// </summary>
public static class Cfgmgr32
{
    public const int CR_SUCCESS = 0;
    public const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    public const uint CM_GETIDLIST_FILTER_CLASS = 0x00000200;
    public const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;

    // DEVICE_POWER_STATE values
    public const int PowerDeviceD0 = 1; // Fully powered

    // DEVPKEY_Device_PowerData: {a45c254e-df1c-4efd-8020-67d146a850e0}, 32
    public static readonly DEVPROPKEY DEVPKEY_Device_PowerData = new DEVPROPKEY
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

