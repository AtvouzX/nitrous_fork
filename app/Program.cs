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

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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
            // If dashboard window is already open, restore and bring to front
            if (BringExistingDashboardToFront())
            {
                return;
            }

            if (isIfeoLaunch)
            {
                EnsureTrayRunning();
            }

            var app = new System.Windows.Application();
            app.Run(new NitrousDashboard());
            return;
        }

        // 2. BACKGROUND ENGINE MODE: Runs purely in the system tray
        mutex = new Mutex(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            // If already running in tray, launching the executable opens the Dashboard
            Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath, "--ui") { UseShellExecute = true });
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!AcerWmiManager.IsHardwareSupported()) return;

        Application.Run(new TrayApplication());
        GC.KeepAlive(mutex);
    }

    private static bool BringExistingDashboardToFront()
    {
        try
        {
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
