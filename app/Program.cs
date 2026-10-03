using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Nitrous.Managers;
using Nitrous.Ui;

namespace Nitrous;

static class Program
{
    private static Mutex? mutex = null;
    private const string AppMutexName = "Nitrous_SingleInstance_Mutex_Lock";

    public const string DashboardMutexName = @"Local\Nitrous_Dashboard_SingleInstance_Mutex";
    public const string DashboardEventName = @"Local\Nitrous_ShowDashboard_Event";

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int ASFW_ANY = -1;

    [STAThread]
    static void Main(string[] args)
    {
        // Check if launched via IFEO debugger
        // When launched by IFEO, args[0] is the path of the intercepted binary (e.g., PSLauncher.exe, NSLauncher.exe, NitroSense.exe)
        bool isIfeoLaunch = false;
        if (args.Length > 0)
        {
            try
            {
                string cleanArg = args[0].Trim('"', ' ');
                string arg0Name = Path.GetFileName(cleanArg);
                foreach (var exe in NitroKeyManager.AcerSenseExecutables)
                {
                    if (string.Equals(arg0Name, exe, StringComparison.OrdinalIgnoreCase))
                    {
                        isIfeoLaunch = true;
                        break;
                    }
                }
            }
            catch { }
        }

        // 1. TRANSIENT UI MODE: Run the dashboard directly
        if ((args.Length > 0 && args[0] == "--ui") || isIfeoLaunch)
        {
            if (isIfeoLaunch)
            {
                EnsureTrayRunning();
            }

            // Strictly enforce single-instance Dashboard UI via named mutex
            using var uiMutex = new Mutex(true, DashboardMutexName, out bool isOnlyUiInstance);
            if (!isOnlyUiInstance)
            {
                // Another UI process is already running or initializing. Signal it to restore and bring to front.
                SignalExistingDashboard();
                return;
            }

            // Create named event to listen for subsequent activation signals (e.g. repeated Nitro button presses)
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, DashboardEventName);

            var app = new System.Windows.Application();
            var dashboard = new NitrousDashboard();

            // Register background wait on the threadpool with zero CPU/thread polling overhead
            var waitHandleReg = ThreadPool.RegisterWaitForSingleObject(showEvent, (state, timedOut) =>
            {
                dashboard.Dispatcher.BeginInvoke(new Action(() =>
                {
                    dashboard.RestoreAndActivate();
                }));
            }, null, Timeout.Infinite, false);

            try
            {
                app.Run(dashboard);
            }
            finally
            {
                waitHandleReg.Unregister(null);
            }

            return;
        }

        // 2. BACKGROUND ENGINE MODE: Runs purely in the system tray
        mutex = new Mutex(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            // If already running in tray, launching the executable opens or restores the Dashboard
            SignalExistingDashboard();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!AcerWmiManager.IsHardwareSupported()) return;

        Application.Run(new TrayApplication());
        GC.KeepAlive(mutex);
    }

    public static void SignalExistingDashboard()
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
        }
        catch { }

        try
        {
            if (EventWaitHandle.TryOpenExisting(DashboardEventName, out var showEvent))
            {
                using (showEvent)
                {
                    showEvent.Set();
                    return;
                }
            }
        }
        catch { }

        // If event isn't ready or UI not running, launch or bring existing window to front
        if (!BringExistingDashboardToFront())
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath, "--ui")
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    public static bool BringExistingDashboardToFront()
    {
        try
        {
            // Fast Win32 lookup by Dashboard window title
            IntPtr hWnd = FindWindow(null, "Nitrous Dashboard");
            if (hWnd != IntPtr.Zero)
            {
                const int SW_RESTORE = 9;
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
                return true;
            }

            using var cur = Process.GetCurrentProcess();
            var list = Process.GetProcessesByName(cur.ProcessName);
            foreach (var p in list)
            {
                using (p)
                {
                    if (p.Id != cur.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        const int SW_RESTORE = 9;
                        ShowWindow(p.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(p.MainWindowHandle);
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private static void EnsureTrayRunning()
    {
        bool trayRunning = Mutex.TryOpenExisting(AppMutexName, out _);
        if (!trayRunning)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
