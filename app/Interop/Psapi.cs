using System;
using System.Runtime.InteropServices;

namespace Nitrous.Interop;

public static class Psapi
{
    [DllImport("psapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int EmptyWorkingSet(IntPtr hwProc);
}

