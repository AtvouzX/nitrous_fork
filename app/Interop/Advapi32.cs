using System;
using System.Runtime.InteropServices;

namespace Nitrous.Interop;

public static class Advapi32
{
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SERVICE_DISABLED = 0x00000004;
    public const uint SERVICE_AUTO_START = 0x00000002;
    public const uint SERVICE_DEMAND_START = 0x00000003;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ChangeServiceConfig(
        IntPtr hService,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);
}
