using System;
using System.Management;
using Microsoft.Win32;

namespace Nitrous.Managers;

public static class SystemInfoManager
{
    private static string? _cachedModel;
    private static string? _cachedManufacturer;
    private static string? _cachedBaseBoard;
    private static string? _cachedBiosVersion;
    private static string? _cachedBiosReleaseDate;
    private static string? _cachedCpuModel;
    private static (string name, string driverVersion)? _cachedGpuInfo;

    public static string GetSystemModel()
    {
        if (_cachedModel != null) return _cachedModel;

        // Fast-path: Read directly from Registry (sub-millisecond, zero COM allocations)
        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
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

    public static string GetSystemManufacturer()
    {
        if (_cachedManufacturer != null) return _cachedManufacturer;

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string? mfg = biosKey?.GetValue("SystemManufacturer")?.ToString();
            if (!string.IsNullOrWhiteSpace(mfg))
            {
                _cachedManufacturer = mfg.Trim();
                return _cachedManufacturer;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"BIOS registry manufacturer lookup failed: {ex.Message}", "SystemInfo");
        }

        _cachedManufacturer = "Unknown Manufacturer";
        return _cachedManufacturer;
    }

    public static string GetBaseBoardProduct()
    {
        if (_cachedBaseBoard != null) return _cachedBaseBoard;

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string? board = biosKey?.GetValue("BaseBoardProduct")?.ToString();
            if (!string.IsNullOrWhiteSpace(board))
            {
                _cachedBaseBoard = board.Trim();
                return _cachedBaseBoard;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"BIOS registry baseboard lookup failed: {ex.Message}", "SystemInfo");
        }

        _cachedBaseBoard = "Unknown Board";
        return _cachedBaseBoard;
    }

    public static string GetBiosVersion()
    {
        if (_cachedBiosVersion != null) return _cachedBiosVersion;

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string? ver = biosKey?.GetValue("BIOSVersion")?.ToString();
            if (!string.IsNullOrWhiteSpace(ver))
            {
                _cachedBiosVersion = ver.Trim();
                return _cachedBiosVersion;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"BIOS registry version lookup failed: {ex.Message}", "SystemInfo");
        }

        _cachedBiosVersion = "Unknown BIOS";
        return _cachedBiosVersion;
    }

    public static string GetBiosReleaseDate()
    {
        if (_cachedBiosReleaseDate != null) return _cachedBiosReleaseDate;

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string? date = biosKey?.GetValue("BIOSReleaseDate")?.ToString();
            if (!string.IsNullOrWhiteSpace(date))
            {
                _cachedBiosReleaseDate = date.Trim();
                return _cachedBiosReleaseDate;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"BIOS registry release date lookup failed: {ex.Message}", "SystemInfo");
        }

        _cachedBiosReleaseDate = "Unknown Date";
        return _cachedBiosReleaseDate;
    }

    public static string GetCpuModel()
    {
        if (_cachedCpuModel != null) return _cachedCpuModel;

        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string? cpu = cpuKey?.GetValue("ProcessorNameString")?.ToString();
            if (!string.IsNullOrWhiteSpace(cpu))
            {
                _cachedCpuModel = cpu.Trim();
                return _cachedCpuModel;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"CPU registry lookup failed: {ex.Message}", "SystemInfo");
        }

        _cachedCpuModel = "Unknown CPU";
        return _cachedCpuModel;
    }

    public static (string name, string driverVersion) GetDiscreteGpuInfo()
    {
        if (_cachedGpuInfo.HasValue) return _cachedGpuInfo.Value;

        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey != null)
            {
                foreach (var subName in classKey.GetSubKeyNames())
                {
                    if (subName.StartsWith("Properties", StringComparison.OrdinalIgnoreCase)) continue;
                    using var subKey = classKey.OpenSubKey(subName);
                    if (subKey == null) continue;

                    string? desc = subKey.GetValue("DriverDesc")?.ToString();
                    string? provider = subKey.GetValue("ProviderName")?.ToString();
                    string? version = subKey.GetValue("DriverVersion")?.ToString();

                    if (!string.IsNullOrWhiteSpace(desc) &&
                        (desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                         desc.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(provider, "NVIDIA", StringComparison.OrdinalIgnoreCase)))
                    {
                        _cachedGpuInfo = (desc.Trim(), !string.IsNullOrWhiteSpace(version) ? version.Trim() : "Unknown Driver");
                        return _cachedGpuInfo.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"GPU registry lookup failed: {ex.Message}", "SystemInfo");
        }

        _cachedGpuInfo = ("NVIDIA Discrete GPU (Not Detected)", "Unknown Driver");
        return _cachedGpuInfo.Value;
    }
}
