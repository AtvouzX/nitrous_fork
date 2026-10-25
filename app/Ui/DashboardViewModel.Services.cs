using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Nitrous.Helpers;
using Nitrous.Managers;
using Nitrous.Mvvm;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace Nitrous.Ui;

public partial class DashboardViewModel
{
    private string _acerServicesStatusText = "Scanning...";

    public string AcerServicesStatusText
    {
        get => _acerServicesStatusText;
        set => SetProperty(ref _acerServicesStatusText, value);
    }

    private string _acerServicesActionText = "STOP & DISABLE";

    public string AcerServicesActionText
    {
        get => _acerServicesActionText;
        set => SetProperty(ref _acerServicesActionText, value);
    }

    public ObservableCollection<AcerServiceInfo> AcerServicesList { get; } = new();

    private Visibility _acerServicesListVisibility = Visibility.Collapsed;

    public Visibility AcerServicesListVisibility
    {
        get => _acerServicesListVisibility;
        set => SetProperty(ref _acerServicesListVisibility, value);
    }

    private string _batteryHealthText = "Evaluating...";

    public string BatteryHealthText
    {
        get => _batteryHealthText;
        set => SetProperty(ref _batteryHealthText, value);
    }

    private string _batteryHealthPercentText = "--%";

    public string BatteryHealthPercentText
    {
        get => _batteryHealthPercentText;
        set => SetProperty(ref _batteryHealthPercentText, value);
    }

    private string _batteryCycleText = "-- cycles";

    public string BatteryCycleText
    {
        get => _batteryCycleText;
        set => SetProperty(ref _batteryCycleText, value);
    }

    public ICommand ToggleAcerServicesCommand { get; private set; } = null!;
    public ICommand KillAcerServiceCommand { get; private set; } = null!;
    public ICommand OpenLogsFolderCommand { get; private set; } = null!;
    public ICommand CopyDiagnosticsCommand { get; private set; } = null!;
    public ICommand ReportIssueCommand { get; private set; } = null!;

    private void InitializeServices()
    {
        ReportIssueCommand = new RelayCommand(_ =>
        {
            try
            {
                string dump = LogManager.ExportSystemDiagnostics();
                Clipboard.SetText(dump);

                string repoUrl = "https://github.com/AtvouzX/nitrous_fork/issues/new";
                string version = $"v{UpdateManager.CurrentVersion}";
                string model = SystemInfoManager.GetSystemModel();

                string query = $"?template=bug_report.yml" +
                               $"&version={Uri.EscapeDataString(version)}" +
                               $"&laptop-model={Uri.EscapeDataString(model)}";

                string issueUrl = $"{repoUrl}{query}";

                Process.Start(new ProcessStartInfo(issueUrl) { UseShellExecute = true });

                ShowGpuAlert(
                    GpuAlertLevel.Info,
                    "Issue Form Opened",
                    "GitHub issue form opened in your browser with hardware specs pre-filled. Complete diagnostics copied to clipboard!",
                    "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3Z");
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to launch issue report: {ex.Message}", "DashboardVM");
            }
        });

        ToggleAcerServicesCommand = new RelayCommand(async _ =>
        {
            AcerServicesActionText = "PLEASE WAIT...";
            var summary = AcerServiceManager.GetServiceSummary();
            if (summary.RunningCount > 0)
            {
                await AcerServiceManager.StopAndDisableAllAsync();
            }
            else
            {
                await AcerServiceManager.RestoreAndStartAllAsync();
            }
            _ = RefreshAcerServicesAsync();
        });

        KillAcerServiceCommand = new RelayCommand(async param =>
        {
            if (param is string serviceName)
            {
                await AcerServiceManager.StopAndDisableSingleAsync(serviceName);
                _ = RefreshAcerServicesAsync();
            }
        });

        OpenLogsFolderCommand = new RelayCommand(_ =>
        {
            try
            {
                string logDir = LogManager.LogsDirectory;
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                if (!File.Exists(explorerPath))
                {
                    explorerPath = Path.Combine(Environment.SystemDirectory, "explorer.exe");
                }
                Process.Start(new ProcessStartInfo(explorerPath, logDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to open logs folder: {ex.Message}", "DashboardVM");
            }
        });

        CopyDiagnosticsCommand = new RelayCommand(_ =>
        {
            try
            {
                string dump = LogManager.ExportSystemDiagnostics();
                Clipboard.SetText(dump);
                ShowGpuAlert(
                    GpuAlertLevel.Info,
                    "Diagnostics Copied",
                    "System diagnostic report has been copied to your clipboard.",
                    "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3Z");
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to copy diagnostics: {ex.Message}", "DashboardVM");
            }
        });

        _ = RefreshBatteryHealthAsync();
        _ = RefreshAcerServicesAsync();
    }

    public async Task RefreshAcerServicesAsync()
    {
        var summary = await Task.Run(() => AcerServiceManager.GetServiceSummary());
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (summary.TotalFound == 0)
            {
                AcerServicesStatusText = "No Acer services found.";
                AcerServicesActionText = "N/A";
                AcerServicesList.Clear();
                AcerServicesListVisibility = Visibility.Collapsed;
            }
            else
            {
                if (summary.RunningCount > 0)
                {
                    AcerServicesStatusText = $"{summary.RunningCount} OEM services running";
                    AcerServicesActionText = "STOP & DISABLE";
                }
                else
                {
                    AcerServicesStatusText = "All Acer services stopped & disabled";
                    AcerServicesActionText = "RESTORE";
                }

                var runningServices = summary.Services.Where(s => s.Status == System.ServiceProcess.ServiceControllerStatus.Running || s.Status == System.ServiceProcess.ServiceControllerStatus.StartPending).ToList();
                if (runningServices.Any())
                {
                    AcerServicesList.Clear();
                    foreach (var s in runningServices)
                    {
                        AcerServicesList.Add(s);
                    }
                    AcerServicesListVisibility = Visibility.Visible;
                }
                else
                {
                    AcerServicesList.Clear();
                    AcerServicesListVisibility = Visibility.Collapsed;
                }
            }
        });
    }

    private async Task RefreshBatteryHealthAsync()
    {
        var health = await Task.Run(() => BatteryManager.GetBatteryHealth());
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (health != null && health.Value.DesignedCapacity > 0)
            {
                var val = health.Value;
                BatteryHealthText = $"{val.FullChargedCapacity:N0} / {val.DesignedCapacity:N0} mWh";
                BatteryHealthPercentText = $"{val.HealthPercent:0.0}%";
                BatteryCycleText = $"{val.CycleCount} cycles";
            }
            else
            {
                BatteryHealthText = "ACPI Data Missing";
                BatteryHealthPercentText = "Unknown Health";
                BatteryCycleText = "Unknown Cycles";
            }
        });
    }
}
