using System;
using System.Diagnostics;
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

    public static bool IsIntegrationEnabled()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(IfeoBasePath);
            if (baseKey == null) return false;

            // Check primary targets
            foreach (var exe in new[] { "PSLauncher.exe", "NSLauncher.exe", "NitroSense.exe", "PredatorSense.exe" })
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
            Debug.WriteLine($"[NitroKeyManager] Failed to check integration: {ex.Message}");
        }

        return false;
    }

    public static bool SetIntegration(bool enable, string exePath)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(IfeoBasePath, true);
            if (baseKey == null) return false;

            if (enable)
            {
                if (string.IsNullOrWhiteSpace(exePath)) return false;

                string vbsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nitrous");
                System.IO.Directory.CreateDirectory(vbsDir);
                string vbsPath = System.IO.Path.Combine(vbsDir, "Launcher.vbs");
                System.IO.File.WriteAllText(vbsPath, "Set WshShell = CreateObject(\"WScript.Shell\")\r\nWshShell.Run \"schtasks /run /tn \"\"Nitrous_Dashboard\"\"\", 0\r\n");

                string safePath = $"wscript.exe \"{vbsPath}\" //B";

                foreach (var exeName in AcerSenseExecutables)
                {
                    using var subKey = baseKey.CreateSubKey(exeName);
                    subKey.SetValue("Debugger", safePath, RegistryValueKind.String);
                }
            }
            else
            {
                foreach (var exeName in AcerSenseExecutables)
                {
                    using var subKey = baseKey.OpenSubKey(exeName, true);
                    if (subKey != null)
                    {
                        subKey.DeleteValue("Debugger", false);
                        // If the subkey has no other values or subkeys, remove it cleanly
                        if (subKey.ValueCount == 0 && subKey.SubKeyCount == 0)
                        {
                            baseKey.DeleteSubKey(exeName, false);
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NitroKeyManager] Failed to set integration ({enable}): {ex.Message}");
            return false;
        }
    }

    public static void SyncExecutablePath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath)) return;

        try
        {
            if (IsIntegrationEnabled())
            {
                SetIntegration(true, currentPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NitroKeyManager] Failed to sync executable path: {ex.Message}");
        }
    }
}
