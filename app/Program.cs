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
    public const string AppMutexName = "Nitrous_SingleInstance_Mutex_Lock";

    public const string DashboardMutexName = @"Local\Nitrous_Dashboard_SingleInstance_Mutex";
    public const string DashboardEventName = @"Local\Nitrous_ShowDashboard_Event";
    public const string SettingsChangedEventName = @"Local\Nitrous_SettingsChanged_Event";

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
        // Parse debug CLI flags early
        bool isDebugArg = false;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--debug", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase))
            {
                isDebugArg = true;
                break;
            }
        }

        // Global crash and unobserved task exception traps
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogManager.Error("Fatal unhandled AppDomain exception", e.ExceptionObject as Exception, "CrashHandler");
            if (TrayApplication.Instance != null)
            {
                TrayApplication.TriggerExitFanReset();
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogManager.Error("Unobserved Task exception", e.Exception, "TaskScheduler");
            e.SetObserved();
        };

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
            catch (Exception ex)
            {
                LogManager.Debug($"Error evaluating IFEO launch arg: {ex.Message}", "Program");
            }
        }

        // 1. TRANSIENT UI MODE: Run the dashboard directly
        if ((args.Length > 0 && (args[0] == "--ui" || Array.Exists(args, a => a == "--ui"))) || isIfeoLaunch)
        {
            LogManager.Initialize(isUiProcess: true, forceDebug: isDebugArg ? true : null);
            EnsureTrayRunning();

            // Strictly enforce single-instance Dashboard UI via named mutex
            using var uiMutex = new Mutex(true, DashboardMutexName, out bool isOnlyUiInstance);
            if (!isOnlyUiInstance)
            {
                LogManager.Info("Another UI process is already active. Signaling existing dashboard.", "Program");
                SignalExistingDashboard();
                return;
            }

            // Create named event to listen for subsequent activation signals (e.g. repeated Nitro button presses)
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, DashboardEventName);

            // Create named event to listen for settings sync signals
            using var syncEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsChangedEventName);

            // Enforce software rendering for WPF UI to ensure the dashboard process NEVER
            // creates Direct3D devices on the discrete NVIDIA GPU or prevents D3Cold sleep.
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            var app = new System.Windows.Application();
            app.DispatcherUnhandledException += (s, e) =>
            {
                LogManager.Error("Unhandled WPF UI Dispatcher exception", e.Exception, "WpfDispatcher");
            };

            var dashboard = new NitrousDashboard();

            // Register background wait on the threadpool with zero CPU/thread polling overhead
            var waitHandleReg = ThreadPool.RegisterWaitForSingleObject(showEvent, (state, timedOut) =>
            {
                dashboard.Dispatcher.BeginInvoke(new Action(() =>
                {
                    dashboard.RestoreAndActivate();
                }));
            }, null, Timeout.Infinite, false);

            var syncHandleReg = ThreadPool.RegisterWaitForSingleObject(syncEvent, (state, timedOut) =>
            {
                LogManager.SyncSettings();
                dashboard.Dispatcher.BeginInvoke(new Action(() =>
                {
                    dashboard.RefreshDashboardState();
                    if (dashboard.DataContext is DashboardViewModel vm)
                    {
                        vm.SyncSettings();
                    }
                }));
            }, null, Timeout.Infinite, false);

            try
            {
                app.Run(dashboard);
            }
            finally
            {
                waitHandleReg.Unregister(null);
                syncHandleReg.Unregister(null);
            }

            return;
        }

        // 2. BACKGROUND ENGINE MODE: Runs purely in the system tray
        LogManager.Initialize(isUiProcess: false, forceDebug: isDebugArg ? true : null);

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            TrayApplication.TriggerExitFanReset();
        };

        mutex = new Mutex(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            LogManager.Info("Tray engine is already running. Signaling existing dashboard.", "Program");
            SignalExistingDashboard();
            return;
        }

        UpdateManager.CleanupOldFiles();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
        {
            LogManager.Error("Unhandled WinForms ThreadException in Tray engine", e.Exception, "WinForms");
            TrayApplication.TriggerExitFanReset();
        };

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!AcerWmiManager.IsHardwareSupported())
        {
            LogManager.Warn("AcerGamingFunction hardware not detected. Nitrous engine shutting down.", "Program");
            return;
        }

        try
        {
            Application.Run(new TrayApplication());
        }
        finally
        {
            TrayApplication.TriggerExitFanReset();
            GC.KeepAlive(mutex);
        }
    }

    public static void SignalExistingDashboard()
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
        }
        catch (Exception ex)
        {
            LogManager.Debug($"AllowSetForegroundWindow failed: {ex.Message}", "Program");
        }

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
        catch (Exception ex)
        {
            LogManager.Debug($"Failed to signal DashboardEvent: {ex.Message}", "Program");
        }

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
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to launch --ui process: {ex.Message}", "Program");
            }
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
        catch (Exception ex)
        {
            LogManager.Debug($"BringExistingDashboardToFront lookup failed: {ex.Message}", "Program");
        }
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
            catch (Exception ex)
            {
                LogManager.Warn($"EnsureTrayRunning failed to launch background tray: {ex.Message}", "Program");
            }
        }
    }
}
