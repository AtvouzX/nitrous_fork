using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace Nitrous.Managers;

public class AcerServiceInfo
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ServiceControllerStatus Status { get; set; }
}

public class AcerServiceSummary
{
    public int TotalFound { get; set; }
    public int RunningCount { get; set; }
    public List<AcerServiceInfo> Services { get; set; } = new();
}

public static class AcerServiceManager
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ChangeServiceConfig(
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

    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    private const uint SERVICE_DISABLED = 0x00000004;
    private const uint SERVICE_AUTO_START = 0x00000002;

    public static readonly string[] TargetServices = new[]
    {
        "NitroSenseService",
        "PredatorSenseService",
        "ACCStd",
        "ACCUserAgent",
        "AcerConfigurationService",
        "AcerAgent",
        "AASSvc",
        "ACCSvc",
        "AcerARTAIMMXDriverService",
        "AcerARTAIMMXService",
        "AcerCCAgentSvis",
        "AcerDeviceEnablingServiceV2",
        "AcerDIAgentSvis",
        "AcerEZSvc",
        "AcerGAICameraService",
        "AcerLightingService",
        "AcerPixyService",
        "AcerQAAgentSvis",
        "AcerServiceSvc",
        "ASMSvc"
    };

    public static AcerServiceSummary GetServiceSummary()
    {
        var summary = new AcerServiceSummary();
        
        try
        {
            var allServices = ServiceController.GetServices();
            foreach (var svc in allServices)
            {
                if (TargetServices.Contains(svc.ServiceName, StringComparer.OrdinalIgnoreCase))
                {
                    summary.TotalFound++;
                    if (svc.Status == ServiceControllerStatus.Running || svc.Status == ServiceControllerStatus.StartPending)
                    {
                        summary.RunningCount++;
                    }
                    
                    summary.Services.Add(new AcerServiceInfo
                    {
                        ServiceName = svc.ServiceName,
                        DisplayName = svc.DisplayName,
                        Status = svc.Status
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enumerate services: {ex.Message}");
        }

        return summary;
    }

    public static async Task StopAndDisableAllAsync()
    {
        await Task.Run(() =>
        {
            var allServices = ServiceController.GetServices();
            foreach (var svc in allServices)
            {
                if (TargetServices.Contains(svc.ServiceName, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        ChangeServiceStartType(svc, SERVICE_DISABLED);

                        if (svc.Status != ServiceControllerStatus.Stopped && svc.Status != ServiceControllerStatus.StopPending)
                        {
                            svc.Stop();
                            svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to stop/disable {svc.ServiceName}: {ex.Message}");
                    }
                }
            }
        });
    }

    public static async Task StopAndDisableSingleAsync(string serviceName)
    {
        await Task.Run(() =>
        {
            var svc = ServiceController.GetServices().FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
            if (svc != null)
            {
                try
                {
                    ChangeServiceStartType(svc, SERVICE_DISABLED);

                    if (svc.Status != ServiceControllerStatus.Stopped && svc.Status != ServiceControllerStatus.StopPending)
                    {
                        svc.Stop();
                        svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to stop/disable {svc.ServiceName}: {ex.Message}");
                }
            }
        });
    }

    public static async Task RestoreAndStartAllAsync()
    {
        await Task.Run(() =>
        {
            var allServices = ServiceController.GetServices();
            foreach (var svc in allServices)
            {
                if (TargetServices.Contains(svc.ServiceName, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        ChangeServiceStartType(svc, SERVICE_AUTO_START);

                        if (svc.Status != ServiceControllerStatus.Running && svc.Status != ServiceControllerStatus.StartPending)
                        {
                            svc.Start();
                            svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(3));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to restore/start {svc.ServiceName}: {ex.Message}");
                    }
                }
            }
        });
    }

    private static void ChangeServiceStartType(ServiceController svc, uint startType)
    {
        try
        {
            bool success = ChangeServiceConfig(
                svc.ServiceHandle.DangerousGetHandle(),
                SERVICE_NO_CHANGE,
                startType,
                SERVICE_NO_CHANGE,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null);

            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"ChangeServiceConfig failed for {svc.ServiceName} with error {error}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Exception changing start type for {svc.ServiceName}: {ex.Message}");
        }
    }
}
