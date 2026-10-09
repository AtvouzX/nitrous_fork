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

    /// <summary>
    /// Safely disposes all ServiceController instances in the specified collection.
    /// </summary>
    public static void DisposeServices(IEnumerable<ServiceController>? services)
    {
        if (services == null) return;
        foreach (var svc in services)
        {
            try
            {
                svc.Dispose();
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to dispose ServiceController: {ex.Message}", "AcerService");
            }
        }
    }

    /// <summary>
    /// Enumerates all Windows services, filtering for target Acer services while immediately disposing all non-matching services.
    /// The caller is responsible for disposing the returned list of services (e.g. via <see cref="DisposeServices"/>).
    /// </summary>
    public static List<ServiceController> GetTargetServices()
    {
        var targetList = new List<ServiceController>();
        ServiceController[]? allServices = null;
        try
        {
            allServices = ServiceController.GetServices();
            foreach (var svc in allServices)
            {
                if (TargetServices.Contains(svc.ServiceName, StringComparer.OrdinalIgnoreCase))
                {
                    targetList.Add(svc);
                }
                else
                {
                    try
                    {
                        svc.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed to enumerate services: {ex.Message}", "AcerService");
            DisposeServices(allServices);
            DisposeServices(targetList);
            targetList.Clear();
        }

        return targetList;
    }

    public static AcerServiceSummary GetServiceSummary()
    {
        var summary = new AcerServiceSummary();
        var targets = GetTargetServices();
        try
        {
            foreach (var svc in targets)
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
        catch (Exception ex)
        {
            LogManager.Warn($"Failed to inspect services: {ex.Message}", "AcerService");
        }
        finally
        {
            DisposeServices(targets);
        }

        return summary;
    }

    public static async Task StopAndDisableAllAsync()
    {
        await Task.Run(() =>
        {
            var targets = GetTargetServices();
            try
            {
                foreach (var svc in targets)
                {
                    try
                    {
                        LogManager.Info($"Stopping and disabling Acer service: {svc.ServiceName}", "AcerService");
                        ChangeServiceStartType(svc, SERVICE_DISABLED);

                        if (svc.Status != ServiceControllerStatus.Stopped && svc.Status != ServiceControllerStatus.StopPending)
                        {
                            svc.Stop();
                            svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Warn($"Failed to stop/disable {svc.ServiceName}: {ex.Message}", "AcerService");
                    }
                }
            }
            finally
            {
                DisposeServices(targets);
            }
        });
    }

    public static async Task StopAndDisableSingleAsync(string serviceName)
    {
        await Task.Run(() =>
        {
            ServiceController[]? allServices = null;
            try
            {
                allServices = ServiceController.GetServices();
                var svc = allServices.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
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
                        LogManager.Warn($"Failed to stop/disable {svc.ServiceName}: {ex.Message}", "AcerService");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to query service {serviceName}: {ex.Message}", "AcerService");
            }
            finally
            {
                DisposeServices(allServices);
            }
        });
    }

    public static async Task RestoreAndStartAllAsync()
    {
        await Task.Run(() =>
        {
            var targets = GetTargetServices();
            try
            {
                foreach (var svc in targets)
                {
                    try
                    {
                        LogManager.Info($"Restoring and starting Acer service: {svc.ServiceName}", "AcerService");
                        ChangeServiceStartType(svc, SERVICE_AUTO_START);

                        if (svc.Status != ServiceControllerStatus.Running && svc.Status != ServiceControllerStatus.StartPending)
                        {
                            svc.Start();
                            svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(3));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Warn($"Failed to restore/start {svc.ServiceName}: {ex.Message}", "AcerService");
                    }
                }
            }
            finally
            {
                DisposeServices(targets);
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
                LogManager.Warn($"ChangeServiceConfig failed for {svc.ServiceName} with Win32 error {error}", "AcerService");
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Exception changing start type for {svc.ServiceName}: {ex.Message}", "AcerService");
        }
    }
}
