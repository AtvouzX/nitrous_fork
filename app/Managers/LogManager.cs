using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Nitrous.Enums;

namespace Nitrous.Managers;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

public static class LogManager
{
    private static readonly object _syncLock = new();
    private static string? _customLogDirectory;
    private static string? _processTag;
    private static bool _initialized;

    public const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB rotation threshold

    public static bool IsInitialized => _initialized;
    public static bool IsDebugEnabled { get; set; }

    public static string LogsDirectory => _customLogDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nitrous",
        "Logs"
    );

    public static string LogFilePath => Path.Combine(LogsDirectory, "nitrous.log");
    public static string RotatedLogFilePath => Path.Combine(LogsDirectory, "nitrous.log.old");

    public static void Initialize(bool isUiProcess = false, bool? forceDebug = null)
    {
        lock (_syncLock)
        {
            int pid = Environment.ProcessId;
            _processTag = isUiProcess ? $"UI:{pid}" : $"TRAY:{pid}";

            if (forceDebug.HasValue)
            {
                IsDebugEnabled = forceDebug.Value;
            }
            else
            {
                SyncSettings();
            }

            _initialized = true;
        }

        Info($"LogManager initialized for {_processTag} (Debug: {IsDebugEnabled})", "LogManager");
    }

    public static void SyncSettings()
    {
        // Check CLI flags, environment variable, or persistent registry setting
        bool envDebug = string.Equals(Environment.GetEnvironmentVariable("NITROUS_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
        bool regDebug = SettingsManager.Get("Debugging", SettingsManager.Get("DebugLogging", false));
        IsDebugEnabled = envDebug || regDebug;
    }

    public static void SetLogDirectoryForTesting(string? testDirectory)
    {
        lock (_syncLock)
        {
            _customLogDirectory = testDirectory;
            _initialized = true;
            if (_processTag == null)
            {
                _processTag = $"TEST:{Environment.ProcessId}";
            }
        }
    }

    public static void Debug(string message, string? tag = null)
    {
        if (!IsDebugEnabled) return;
        WriteEntry(LogLevel.Debug, message, tag);
    }

    public static void Info(string message, string? tag = null)
    {
        WriteEntry(LogLevel.Info, message, tag);
    }

    public static void Warn(string message, string? tag = null)
    {
        WriteEntry(LogLevel.Warn, message, tag);
    }

    public static void Error(string message, Exception? ex = null, string? tag = null)
    {
        string fullMessage = ex != null
            ? $"{message} Exception: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}"
            : message;

        WriteEntry(LogLevel.Error, fullMessage, tag);
    }

    private static void WriteEntry(LogLevel level, string message, string? tag)
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Info  => "INF",
                LogLevel.Warn  => "WRN",
                LogLevel.Error => "ERR",
                _ => "UNK"
            };

            string proc = _processTag ?? $"PID:{Environment.ProcessId}";
            string tagPart = string.IsNullOrWhiteSpace(tag) ? "" : $" [{tag}]";
            string logLine = $"[{timestamp}] [{levelStr}] [{proc}]{tagPart} {message}";

            lock (_syncLock)
            {
                WriteToFileWithRetry(logLine);
            }
        }
        catch (Exception ex)
        {
            // Logging must never crash the host application.
            System.Diagnostics.Debug.WriteLine($"[LogManager] Failed to write log: {ex.Message}");
        }
    }

    private static void WriteToFileWithRetry(string line)
    {
        string logPath = LogFilePath;
        string logDir = LogsDirectory;

        try
        {
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            CheckAndRotateLog(logPath);

            // Cross-process non-locking write with up to 3 retries
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        logPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        bufferSize: 4096,
                        FileOptions.None
                    );
                    using var writer = new StreamWriter(stream, Encoding.UTF8);
                    writer.WriteLine(line);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(5);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LogManager] WriteToFileWithRetry error: {ex.Message}");
        }
    }

    private static void CheckAndRotateLog(string logPath)
    {
        try
        {
            var fileInfo = new FileInfo(logPath);
            if (fileInfo.Exists && fileInfo.Length >= MaxLogFileSizeBytes)
            {
                string oldPath = RotatedLogFilePath;
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
                File.Move(logPath, oldPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LogManager] Log rotation error: {ex.Message}");
        }
    }

    public static string ExportSystemDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("NITROUS DIAGNOSTIC HARDWARE DUMP");
        sb.AppendLine($"Timestamp (UTC):     {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"App Version:         {UpdateManager.CurrentVersion} (net10.0-windows, {RuntimeInformation.ProcessArchitecture})");
        sb.AppendLine($"Windows OS:          {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Elevation:           {(IsUserAdministrator() ? "Administrator (Elevated)" : "Standard User (Non-Elevated)")}");
        sb.AppendLine($"Process Context:     {_processTag ?? $"PID:{Environment.ProcessId}"}");
        sb.AppendLine();

        sb.AppendLine("[SYSTEM & HARDWARE SPECS]");
        try
        {
            string model = SystemInfoManager.GetSystemModel();
            string mfg = SystemInfoManager.GetSystemManufacturer();
            string board = SystemInfoManager.GetBaseBoardProduct();
            sb.AppendLine($"Laptop Model:        {model} ({mfg}, Board: {board})");

            string biosVer = SystemInfoManager.GetBiosVersion();
            string biosDate = SystemInfoManager.GetBiosReleaseDate();
            sb.AppendLine($"BIOS Version:        {biosVer} (Date: {biosDate})");

            string cpu = SystemInfoManager.GetCpuModel();
            sb.AppendLine($"CPU Model:           {cpu}");

            var (gpuName, gpuDriver) = SystemInfoManager.GetDiscreteGpuInfo();
            sb.AppendLine($"Discrete GPU:        {gpuName} (Driver: {gpuDriver})");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Hardware Specs:      Error probing ({ex.Message})");
        }

        sb.AppendLine();
        sb.AppendLine("[HARDWARE & ACER EC]");
        try
        {
            bool supported = AcerWmiManager.IsHardwareSupported();
            sb.AppendLine($"AcerGamingFunction:  {(supported ? "Supported (root\\wmi)" : "Unavailable / Not Detected")}");
            if (supported)
            {
                sb.AppendLine($"Turbo Mode Bit 5:    {(AcerWmiManager.IsTurboModeSupported() ? "Supported" : "Unsupported")}");
                var activePower = AcerWmiManager.GetActivePowerMode();
                sb.AppendLine($"Active Power Mode:   {(activePower.HasValue ? activePower.Value.ToString() : "Unknown")}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"AcerGamingFunction:  Error probing ({ex.Message})");
        }

        sb.AppendLine();
        sb.AppendLine("[DISCRETE GPU STATE]");
        try
        {
            bool awake = NvidiaGpuManager.IsGpuAwake();
            sb.AppendLine($"dGPU State:          {(awake ? "Active / D0 Awake" : "D3Cold Sleeping")}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"dGPU State:          Error probing ({ex.Message})");
        }

        sb.AppendLine();
        sb.AppendLine("[POWER & BATTERY]");
        try
        {
            var pwr = SystemInformation.PowerStatus;
            string pwrSource = pwr.PowerLineStatus == PowerLineStatus.Online ? "AC Power (Charger plugged in)" : "Battery (DC Power)";
            sb.AppendLine($"Power Source:        {pwrSource}");
            sb.AppendLine($"Power Line:          {pwr.PowerLineStatus}");
            sb.AppendLine($"Battery Charge:      {(int)(pwr.BatteryLifePercent * 100)}%");
            sb.AppendLine($"Battery Status:      {pwr.BatteryChargeStatus}");
            sb.AppendLine($"80% Charge Limit:    {SettingsManager.Get("ChargeLimit", false)}");
            sb.AppendLine($"Manage CPU Power:    {SettingsManager.Get("ManageCpuPower", true)}");
            sb.AppendLine($"Debugging Active:    {IsDebugEnabled}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Power Status:        Error reading ({ex.Message})");
        }

        sb.AppendLine();
        sb.AppendLine("[RECENT DIAGNOSTIC LOGS]");
        try
        {
            sb.AppendLine(GetRecentLogTail(15));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Logs:                Error reading ({ex.Message})");
        }

        sb.AppendLine("================================================================================");
        return sb.ToString();
    }

    public static string GetRecentLogTail(int maxLines = 15)
    {
        try
        {
            string path = LogFilePath;
            if (!File.Exists(path))
            {
                return "(No active log file found)";
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                lines.Add(line);
                if (lines.Count > maxLines * 3)
                {
                    lines.RemoveRange(0, lines.Count - maxLines);
                }
            }

            int count = Math.Min(maxLines, lines.Count);
            if (count == 0) return "(Active log file is empty)";
            var tail = lines.Skip(lines.Count - count).ToArray();
            return string.Join(Environment.NewLine, tail);
        }
        catch (Exception ex)
        {
            return $"(Could not read log tail: {ex.Message})";
        }
    }

    private static bool IsUserAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
