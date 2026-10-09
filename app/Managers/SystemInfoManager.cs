using System.Management;

namespace Nitrous.Managers;

public static class SystemInfoManager
{
    private static string? _cachedModel;

    public static string GetSystemModel()
    {
        if (_cachedModel != null) return _cachedModel;

        // Fast-path: Read directly from Registry (sub-millisecond, zero COM allocations)
        try
        {
            using var biosKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string? prodName = biosKey?.GetValue("SystemProductName")?.ToString();
            if (!string.IsNullOrWhiteSpace(prodName))
            {
                _cachedModel = prodName.Trim();
                return _cachedModel;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"BIOS registry model lookup failed: {ex.Message}", "SystemInfo");
        }

        // Fallback: WMI query
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            using var collection = searcher.Get();
            foreach (ManagementBaseObject item in collection)
            {
                using (item)
                {
                    _cachedModel = item["Model"]?.ToString() ?? "Unknown System";
                    LogManager.Info($"System model detected via WMI: {_cachedModel}", "SystemInfo");
                    return _cachedModel;
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"WMI model query failed: {ex.Message}", "SystemInfo");
        }
        return "Unknown System";
    }
}
