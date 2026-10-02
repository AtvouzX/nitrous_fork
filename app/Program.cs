using System;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media;
using Nitrous.Managers;
using Nitrous.Ui;

namespace Nitrous;

static class Program
{
    private static Mutex? mutex = null;

    [STAThread]
    static void Main(string[] args)
    {
        // TRANSIENT UI MODE: Run the dashboard in its own temporary process
        if (args.Length > 0 && args[0] == "--ui")
        {
            var app = new System.Windows.Application();
            app.Run(new NitrousDashboard());
            return;
        }

        // BACKGROUND ENGINE MODE: Runs purely in the system tray
        const string appName = "Nitrous_SingleInstance_Mutex_Lock";
        mutex = new Mutex(true, appName, out bool createdNew);
        if (!createdNew) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!AcerWmiManager.IsHardwareSupported()) return;

        Application.Run(new TrayApplication());
        GC.KeepAlive(mutex);
    }
}
