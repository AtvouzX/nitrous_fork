using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nitrous.Enums;
using Nitrous.Managers;

namespace Nitrous.Diagnostics;

public sealed record EcProbeResult(string JsonPath, string SummaryPath, int ErrorCount);

/// <summary>
/// Read-only fingerprint of the Acer EC/firmware contract.
/// Probes machine identity, capabilities, and registers via Get-only calls.
/// Never issues Set* commands or wakes the dGPU.
/// </summary>
public static partial class EcProbe
{
    private const string ReadOnlyPrefix = "Get";

    private static readonly (string Name, uint Address, ulong Mask)[] KnownSensors =
    {
        ("cpuTemperature", AcerWmiProtocol.CpuTemperature, AcerWmiProtocol.ByteMask),
        ("cpuFanRpm", AcerWmiProtocol.CpuFanRpm, AcerWmiProtocol.WordMask),
        ("gpuFanRpm", AcerWmiProtocol.GpuFanRpm, AcerWmiProtocol.WordMask),
        ("gpuTemperaturePrimary", AcerWmiProtocol.GpuTemperaturePrimary, AcerWmiProtocol.ByteMask),
        ("gpuTemperatureFallbackA", AcerWmiProtocol.GpuTemperatureFallbackA, AcerWmiProtocol.ByteMask),
        ("gpuTemperatureFallbackB", AcerWmiProtocol.GpuTemperatureFallbackB, AcerWmiProtocol.ByteMask),
    };

    private static readonly string[] ProbedClasses =
    {
        AcerWmiProtocol.GamingClass,
        AcerWmiProtocol.ActionClass,
        "AcerGenericMethod",
        AcerWmiProtocol.BatteryClass,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string OutputDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nitrous", "ec-probe");

    internal static bool IsReadOnlyMethod(string methodName) =>
        methodName.StartsWith(ReadOnlyPrefix, StringComparison.OrdinalIgnoreCase);

    public static Task<EcProbeResult> RunAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Run(cancellationToken), cancellationToken);

    private static EcProbeResult Run(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(OutputDirectory);

        var errors = new List<string>();
        var report = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["probeVersion"] = 10,
            ["capturedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            ["readOnly"] = true,
            ["note"] = "Read-only Acer EC/firmware fingerprint. No Set* method is called by this probe."
        };

        report["machine"] = CollectMachine(errors);
        cancellationToken.ThrowIfCancellationRequested();

        report["capabilities"] = CollectCapabilities(errors);
        cancellationToken.ThrowIfCancellationRequested();

        report["classes"] = CollectClasses(errors);
        cancellationToken.ThrowIfCancellationRequested();

        report["ecHid"] = CollectEcHid(errors);
        cancellationToken.ThrowIfCancellationRequested();

        report["sensors"] = CollectSensors(errors);
        cancellationToken.ThrowIfCancellationRequested();

        report["miscSettings"] = CollectMiscSettings(errors);
        cancellationToken.ThrowIfCancellationRequested();

        report["profiles"] = CollectProfiles(errors);
        cancellationToken.ThrowIfCancellationRequested();

        report["genericFunction"] = CollectGenericFunction(errors);
        report["keyboardBacklight"] = CollectKeyboardBacklight(errors);
        report["ledState"] = CollectLedState(errors);
        report["fans"] = CollectFans(errors);
        report["battery"] = CollectBattery(errors);
        report["keyboardAutoOff"] = CollectKeyboardAutoOff(errors);
        report["hidDevices"] = CollectHidDevices(errors);

        report["errors"] = errors.ToArray();

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var jsonPath = Path.Combine(OutputDirectory, $"ec-probe-{stamp}.json");
        var summaryPath = Path.Combine(OutputDirectory, $"ec-probe-{stamp}-summary.txt");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(summaryPath, BuildSummary(report, errors));

        LogManager.Info($"EC Probe complete: written to {jsonPath} ({errors.Count} non-fatal errors)", "EcProbe");
        return new EcProbeResult(jsonPath, summaryPath, errors.Count);
    }

    private static Dictionary<string, object?> CollectCapabilities(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var capabilities = AcerCapabilitiesProvider.Current;
            result["powerModeChannel"] = capabilities.PowerModeChannel.ToString();
            result["canSetPowerMode"] = capabilities.CanSetPowerMode;
            result["supportsTurbo"] = capabilities.SupportsTurbo;
            result["wmiGamingClassPresent"] = capabilities.Wmi.GamingClassPresent;
            result["wmiActionClassPresent"] = capabilities.Wmi.ActionClassPresent;
            result["wmiGamingMethods"] = capabilities.Wmi.GamingMethods.ToArray();
            result["wmiSupportedProfiles"] = capabilities.Wmi.SupportedProfiles
                .Select(profile => profile.ToString()).ToArray();
            result["apgeActionFunction"] = capabilities.Wmi.ActionFunction.ToString();
            result["smbiosGamingVersion"] = capabilities.Smbios.GamingVersion;
            result["smbiosOperatingModesListed"] = capabilities.Smbios.OperatingModesListed;
            result["smbiosKeyboardColor"] = capabilities.Smbios.KeyboardColor;
            result["smbiosEcLightBars"] = capabilities.Smbios.HasEcLightBars;
            result["smbiosLedArrayLength"] = capabilities.Smbios.LedArrayLength;
            result["smbiosHotkeyFunctions"] = capabilities.Smbios.HotkeyFunctions.Count;
            result["modelProfile"] = capabilities.Profile.ModelCode ?? "generic";
            result["requiresBalancedModeReset"] = capabilities.Profile.RequiresBalancedModeReset;
            result["summary"] = capabilities.Describe();
        }
        catch (Exception ex)
        {
            result["error"] = ex.Message;
            errors.Add($"capabilities: {ex.Message}");
        }

        return result;
    }

    private static Dictionary<string, object?> CollectMachine(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        void Put(string key, Func<object?> read)
        {
            try { result[key] = read(); }
            catch (Exception ex) { errors.Add($"{key}: {ex.Message}"); }
        }

        Put("manufacturer", () => QueryFirst("Win32_ComputerSystem", "Manufacturer"));
        Put("model", () => QueryFirst("Win32_ComputerSystem", "Model"));
        Put("productName", () => QueryFirst("Win32_ComputerSystemProduct", "Name"));
        Put("productVersion", () => QueryFirst("Win32_ComputerSystemProduct", "Version"));
        Put("biosVersion", () => QueryFirst("Win32_BIOS", "SMBIOSBIOSVersion"));
        Put("biosReleaseDate", () => QueryFirst("Win32_BIOS", "ReleaseDate") is string d && d.Length >= 8
            ? $"{d[..4]}-{d[4..6]}-{d[6..8]}"
            : null);
        Put("osVersion", () => Environment.OSVersion.VersionString);
        Put("osArchitecture", () => Environment.Is64BitOperatingSystem ? "x64" : "x86");
        Put("isElevated", () => IsElevated());

        Put("gpuName", () => QueryFirst("Win32_VideoController", "Name"));
        Put("gpuDriverVersion", () => QueryFirst("Win32_VideoController", "DriverVersion"));

        return result;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? QueryFirst(string wmiClass, string property)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
        foreach (ManagementObject row in searcher.Get())
        {
            using (row)
            {
                return row[property]?.ToString();
            }
        }

        return null;
    }

    private static Dictionary<string, object?> CollectClasses(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var className in ProbedClasses)
        {
            var entry = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var managementClass = new ManagementClass(
                    AcerWmiProtocol.WmiNamespace, className, null);

                entry["present"] = true;
                entry["methods"] = managementClass.Methods
                    .Cast<MethodData>()
                    .Select(method => new Dictionary<string, object?>
                    {
                        ["name"] = method.Name,
                        ["wmiMethodId"] = QualifierValue(method.Qualifiers, "WmiMethodId"),
                        ["parameters"] = method.InParameters?.Properties
                            .Cast<PropertyData>()
                            .Select(p => $"{p.Name}:{p.Type}")
                            .ToArray() ?? Array.Empty<string>()
                    })
                    .OrderBy(m => m["name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                entry["instanceName"] = TryGetInstanceName(className);
            }
            catch (Exception ex)
            {
                entry["present"] = false;
                entry["error"] = ex.Message;
                errors.Add($"{className}: {ex.Message}");
            }

            result[className] = entry;
        }

        return result;
    }

    private static object? QualifierValue(QualifierDataCollection? qualifiers, string name)
    {
        if (qualifiers == null) return null;
        foreach (QualifierData qualifier in qualifiers)
        {
            if (string.Equals(qualifier.Name, name, StringComparison.OrdinalIgnoreCase))
                return qualifier.Value?.ToString();
        }

        return null;
    }

    private static string? TryGetInstanceName(string className)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                AcerWmiProtocol.WmiNamespace, $"SELECT * FROM {className}");
            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    return instance["InstanceName"]?.ToString() ?? instance.Path?.ToString();
                }
            }
        }
        catch { }

        return null;
    }

    private static Dictionary<string, object?> CollectEcHid(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var hid = AcerEcHid.Open();
            if (hid == null)
            {
                result["present"] = false;
                result["note"] = "No Acer EC HID interface (VID 1025 / PID 174B, usage page 0xFF05) was found.";
                return result;
            }

            result["present"] = true;
            result["devicePath"] = hid.DevicePath;
            result["vendorId"] = $"0x{hid.VendorId:X4}";
            result["productId"] = $"0x{hid.ProductId:X4}";
            result["usagePage"] = $"0x{hid.UsagePage:X4}";
            result["usage"] = $"0x{hid.Usage:X4}";
            result["featureLength"] = hid.FeatureLength;
            result["version"] = hid.Version;
            result["modeCapability"] = hid.Capability;
            result["modes"] = hid.Modes.Select(mode => mode.ToString()).ToArray();
            result["currentMode"] = hid.ReadMode()?.ToString();
        }
        catch (Exception ex)
        {
            result["present"] = false;
            result["error"] = ex.Message;
            errors.Add($"ecHid: {ex.Message}");
        }

        return result;
    }

    private static string BuildSummary(Dictionary<string, object?> report, List<string> errors)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("NITROUS READ-ONLY EC/FIRMWARE PROBE SUMMARY");
        sb.AppendLine($"Captured: {report.GetValueOrDefault("capturedAt")}");

        if (report.GetValueOrDefault("machine") is Dictionary<string, object?> machine)
        {
            sb.AppendLine($"Machine: {machine.GetValueOrDefault("manufacturer")} {machine.GetValueOrDefault("model")}");
            sb.AppendLine($"BIOS: {machine.GetValueOrDefault("biosVersion")} ({machine.GetValueOrDefault("biosReleaseDate")})");
            sb.AppendLine($"OS: {machine.GetValueOrDefault("osVersion")} ({machine.GetValueOrDefault("osArchitecture")})");
        }

        if (report.GetValueOrDefault("capabilities") is Dictionary<string, object?> caps)
        {
            sb.AppendLine($"Power Mode Channel: {caps.GetValueOrDefault("powerModeChannel")}");
            sb.AppendLine($"Turbo Mode Supported: {caps.GetValueOrDefault("supportsTurbo")}");
            sb.AppendLine($"Summary: {caps.GetValueOrDefault("summary")}");
        }

        sb.AppendLine($"Non-fatal errors encountered: {errors.Count}");
        foreach (var err in errors.Take(10))
            sb.AppendLine($"  - {err}");

        return sb.ToString();
    }
}
