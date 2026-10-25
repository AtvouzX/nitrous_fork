using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Nitrous.Managers;

public static class StartupManager
{
    public static readonly string CachedSchtasksPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    public static bool CheckStartupTask()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(CachedSchtasksPath, "/query /tn \"Nitrous\"") { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit();
            return p != null && p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            LogManager.Warn($"CheckStartupTask failed: {ex.Message}", "Startup");
            return false;
        }
    }

    public static bool ToggleStartupTask(bool enable, string exePath)
    {
        try
        {
            if (enable)
            {
                string escapedExePath = System.Security.SecurityElement.Escape(exePath);
                string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?><Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task""><Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT5S</Delay></LogonTrigger></Triggers><Principals><Principal id=""Author""><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><RunOnlyIfIdle>false</RunOnlyIfIdle></Settings><Actions Context=""Author""><Exec><Command>{escapedExePath}</Command></Exec></Actions></Task>";
                string tempFile = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(tempFile, xml, Encoding.Unicode);
                    using var p = Process.Start(new ProcessStartInfo(CachedSchtasksPath, $"/create /tn \"Nitrous\" /xml \"{tempFile}\" /f") { CreateNoWindow = true, UseShellExecute = false });
                    p?.WaitForExit();

                    string xmlDashboard = $@"<?xml version=""1.0"" encoding=""UTF-16""?><Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task""><Triggers><LogonTrigger><Enabled>false</Enabled></LogonTrigger></Triggers><Principals><Principal id=""Author""><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><RunOnlyIfIdle>false</RunOnlyIfIdle></Settings><Actions Context=""Author""><Exec><Command>{escapedExePath}</Command><Arguments>--ui</Arguments></Exec></Actions></Task>";
                    File.WriteAllText(tempFile, xmlDashboard, Encoding.Unicode);
                    using var p2 = Process.Start(new ProcessStartInfo(CachedSchtasksPath, $"/create /tn \"Nitrous_Dashboard\" /xml \"{tempFile}\" /f") { CreateNoWindow = true, UseShellExecute = false });
                    p2?.WaitForExit();

                    bool success = p != null && p.ExitCode == 0;
                    LogManager.Info($"ToggleStartupTask(true) completed. Success: {success}", "Startup");
                    return success;
                }
                finally
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                }
            }
            else
            {
                using var p = Process.Start(new ProcessStartInfo(CachedSchtasksPath, "/delete /tn \"Nitrous\" /f") { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit();
                using var p2 = Process.Start(new ProcessStartInfo(CachedSchtasksPath, "/delete /tn \"Nitrous_Dashboard\" /f") { CreateNoWindow = true, UseShellExecute = false });
                p2?.WaitForExit();

                bool success = p != null && p.ExitCode == 0;
                LogManager.Info($"ToggleStartupTask(false) completed. Success: {success}", "Startup");
                return success;
            }
        }
        catch (Exception ex)
        {
            LogManager.Error($"ToggleStartupTask({enable}) failed: {ex.Message}", ex, "Startup");
            return false;
        }
    }
}
