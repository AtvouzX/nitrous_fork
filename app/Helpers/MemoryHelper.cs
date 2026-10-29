using System.Diagnostics;
using Nitrous.Interop;
using Nitrous.Managers;

namespace Nitrous.Helpers;

public static class MemoryHelper
{
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
            Psapi.EmptyWorkingSet(currentProcess.Handle);
        }
        catch (Exception ex)
        {
            LogManager.Debug($"TrimWorkingSet failed: {ex.Message}", "Memory");
        }
    }
}
