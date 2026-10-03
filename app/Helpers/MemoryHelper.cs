using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nitrous.Helpers;

public static class MemoryHelper
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    /// <summary>
    /// Flushes unneeded working set memory pages back to the operating system after JIT/startup finishes.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);

            using var currentProcess = Process.GetCurrentProcess();
            EmptyWorkingSet(currentProcess.Handle);
        }
        catch
        {
            // Fail silently if process permissions restrict working set modification
        }
    }
}
