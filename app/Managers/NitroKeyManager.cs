using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Nitrous.Managers;

public static class NitroKeyManager
{
    private const string IfeoBasePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    public static readonly string[] AcerSenseExecutables =
    [
        "PSLauncher.exe",
        "NSLauncher.exe",
        "NitroSense.exe",
        "NitroSenseV3.exe",
        "NitroSenseV4.exe",
        "PredatorSense.exe",
        "PredatorSenseV3.exe",
        "PredatorSenseV4.exe"
    ];

    public static string FormatSafePath(string path)
    {
        return $"\"{path.Trim('\"', ' ')}\"";
    }

    /// <summary>
    /// Checks if any legacy IFEO debugger integration exists in HKLM.
    /// </summary>
    public static bool IsLegacyIfeoPresent()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(IfeoBasePath);
            if (baseKey == null) return false;

            foreach (var exe in AcerSenseExecutables)
            {
                using var sub = baseKey.OpenSubKey(exe);
                if (sub?.GetValue("Debugger") is string val && !string.IsNullOrWhiteSpace(val))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NitroKeyManager] Failed checking legacy IFEO: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Legacy compatibility check: returns false since IFEO is retired in favor of native NitroKeyHook.
    /// </summary>
    public static bool IsIntegrationEnabled() => false;

    /// <summary>
    /// Compatibility stub: ensuring IFEO is permanently purged and physical button handling is native.
    /// </summary>
    public static bool SetIntegration(bool enable, string exePath)
    {
        PurgeLegacyIfeoIntegration();
        return true;
    }

    public static void SyncExecutablePath(string currentPath)
    {
        // No-op: IFEO is retired; NitroKeyHook handles physical key presses directly.
    }

    /// <summary>
    /// Completely removes legacy IFEO registry hooks, deletes zero-byte dummy files from System32,
    /// and removes legacy Launcher.vbs scripts from user AppData.
    /// Physical button presses are handled natively in-process by NitroKeyHook (WH_KEYBOARD_LL).
    /// </summary>
    public static void PurgeLegacyIfeoIntegration()
    {
        // 1. Clean up HKLM Image File Execution Options registry keys
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(IfeoBasePath, true);
            if (baseKey != null)
            {
                foreach (var exeName in AcerSenseExecutables)
                {
                    using var subKey = baseKey.OpenSubKey(exeName, true);
                    if (subKey != null)
                    {
                        subKey.DeleteValue("Debugger", false);
                        if (subKey.ValueCount == 0 && subKey.SubKeyCount == 0)
                        {
                            baseKey.DeleteSubKey(exeName, false);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NitroKeyManager] Failed purging legacy IFEO registry entries: {ex.Message}");
        }

        // 2. Remove any legacy zero-byte dummy files previously placed in System32
        try
        {
            string sysDir = Environment.SystemDirectory;
            foreach (var exeName in AcerSenseExecutables)
            {
                string dummyPath = Path.Combine(sysDir, exeName);
                if (File.Exists(dummyPath))
                {
                    var fi = new FileInfo(dummyPath);
                    if (fi.Length == 0)
                    {
                        File.Delete(dummyPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NitroKeyManager] Failed removing legacy dummy files from System32: {ex.Message}");
        }

        // 3. Remove legacy Launcher.vbs script from user AppData
        try
        {
            string vbsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nitrous");
            string vbsPath = Path.Combine(vbsDir, "Launcher.vbs");
            if (File.Exists(vbsPath))
            {
                File.Delete(vbsPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NitroKeyManager] Failed removing legacy Launcher.vbs: {ex.Message}");
        }

        SettingsManager.Delete("NitroKeyIntegrated");
    }
}
