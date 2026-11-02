using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using Nitrous.Enums;

namespace Nitrous.Managers;

internal enum PowerModeChannel
{
    None,
    EcHid,
    Wmi,
    ActionFunction7
}

internal enum AcerActionFunction
{
    Unknown,
    CoolBoost,
    OperatingMode
}

internal sealed record AcerWmiSurface(
    bool GamingClassPresent,
    bool ActionClassPresent,
    IReadOnlyList<string> GamingMethods,
    IReadOnlyList<PowerProfile> SupportedProfiles,
    AcerActionFunction ActionFunction)
{
    public static AcerWmiSurface Empty { get; } = new(
        false, false, Array.Empty<string>(), Array.Empty<PowerProfile>(), AcerActionFunction.Unknown);
}

internal sealed record AcerCapabilities(
    string? Manufacturer,
    string? Model,
    string? BiosVersion,
    AcerSmbios Smbios,
    AcerWmiSurface Wmi,
    EcHidInfo? EcHid,
    AcerModelProfile Profile)
{
    public PowerModeChannel PowerModeChannel =>
        EcHid is { Modes.Count: > 0 } ? PowerModeChannel.EcHid
        : Wmi.SupportedProfiles.Count > 0 ? PowerModeChannel.Wmi
        : Wmi.ActionFunction == AcerActionFunction.OperatingMode ? PowerModeChannel.ActionFunction7
        : PowerModeChannel.None;

    public bool CanSetPowerMode => PowerModeChannel is PowerModeChannel.EcHid or PowerModeChannel.Wmi;

    public bool SupportsTurbo => PowerModeChannel switch
    {
        PowerModeChannel.EcHid => EcHid is { } hid && hid.Modes.Contains(PowerProfile.Turbo),
        PowerModeChannel.Wmi => Wmi.SupportedProfiles.Contains(PowerProfile.Turbo),
        _ => false
    };

    public string Describe() =>
        $"model {Model ?? "unknown"} ({BiosVersion ?? "unknown BIOS"}); " +
        $"channel {PowerModeChannel}; " +
        $"ec hid {(EcHid is null ? "absent" : $"{EcHid.Version ?? "?"} modes [{string.Join(", ", EcHid.Modes)}]")}; " +
        $"wmi modes [{string.Join(", ", Wmi.SupportedProfiles)}]; " +
        $"apge action {Wmi.ActionFunction}; " +
        Smbios.Describe();
}

internal static class AcerCapabilitiesProvider
{
    private static readonly object Gate = new();
    private static AcerCapabilities? _cached;
    private static AcerCapabilities? _testOverride;

    internal static void SetTestCapabilities(AcerCapabilities? overrideCaps)
    {
        lock (Gate)
        {
            _testOverride = overrideCaps;
        }
    }

    public static AcerCapabilities Current
    {
        get
        {
            lock (Gate)
            {
                if (_testOverride != null) return _testOverride;
                return _cached ??= Probe();
            }
        }
    }

    public static AcerCapabilities Refresh()
    {
        lock (Gate)
        {
            if (_testOverride != null) return _testOverride;
            _cached = Probe();
            return _cached;
        }
    }

    public static void Invalidate()
    {
        lock (Gate) _cached = null;
    }

    private static AcerCapabilities Probe()
    {
        var (manufacturer, model, bios) = ReadIdentity();
        var smbios = AcerSmbios.Read();
        var wmi = ProbeWmi();
        var ecHid = ProbeEcHid();

        var capabilities = new AcerCapabilities(
            manufacturer, model, bios, smbios, wmi, ecHid, AcerModelProfile.For(model));

        LogManager.Info($"Capabilities: {capabilities.Describe()}", "Capabilities");

        if (capabilities.PowerModeChannel == PowerModeChannel.ActionFunction7)
        {
            LogManager.Warn(
                "Capabilities: operating mode is on APGeAction function 7. Path is read-only on this model.",
                "Capabilities");
        }

        return capabilities;
    }

    private static (string? Manufacturer, string? Model, string? Bios) ReadIdentity()
    {
        string? manufacturer = null, model = null, bios = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject row in searcher.Get())
            {
                using (row)
                {
                    manufacturer = row["Manufacturer"]?.ToString();
                    model = row["Model"]?.ToString();
                }
                break;
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Could not read machine model: {ex.Message}", "Capabilities");
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (ManagementObject row in searcher.Get())
            {
                using (row)
                {
                    bios = row["SMBIOSBIOSVersion"]?.ToString();
                }
                break;
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Could not read BIOS version: {ex.Message}", "Capabilities");
        }

        return (manufacturer, model, bios);
    }

    private static AcerWmiSurface ProbeWmi()
    {
        bool gamingPresent = false;
        bool actionPresent = false;
        var methods = new List<string>();
        var profiles = new List<PowerProfile>();
        var action = AcerActionFunction.Unknown;

        try
        {
            using var gamingClass = new ManagementClass(
                AcerWmiProtocol.WmiNamespace, AcerWmiProtocol.GamingClass, null);
            gamingClass.Get();
            gamingPresent = true;

            foreach (var method in gamingClass.Methods)
                methods.Add(method.Name);
        }
        catch
        {
            gamingPresent = false;
        }

        if (gamingPresent)
        {
            profiles.AddRange(ReadSupportedProfiles());
        }

        try
        {
            using var actionClass = new ManagementClass(AcerWmiProtocol.WmiNamespace, AcerWmiProtocol.ActionClass, null);
            actionClass.Get();
            actionPresent = true;
            action = ReadActionFunction();
        }
        catch
        {
            actionPresent = false;
        }

        return new AcerWmiSurface(gamingPresent, actionPresent, methods, profiles, action);
    }

    private static IReadOnlyList<PowerProfile> ReadSupportedProfiles()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                AcerWmiProtocol.WmiNamespace,
                $"SELECT * FROM {AcerWmiProtocol.GamingClass}");

            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    using var inParams = instance.GetMethodParameters(AcerWmiProtocol.GetGamingMiscSetting);
                    inParams["gmInput"] = AcerWmiProtocol.SupportedProfilesSetting;
                    using var outParams = instance.InvokeMethod(
                        AcerWmiProtocol.GetGamingMiscSetting, inParams, null);

                    var raw = outParams?.Properties["gmOutput"]?.Value;
                    if (raw == null) return Array.Empty<PowerProfile>();

                    var mask = (int)((Convert.ToUInt64(raw, CultureInfo.InvariantCulture) >> 8) & 0xFF);
                    return Enum.GetValues<PowerProfile>()
                        .Where(profile => (mask & (1 << (int)profile)) != 0)
                        .ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Could not read supported power profiles: {ex.Message}", "Capabilities");
        }

        return Array.Empty<PowerProfile>();
    }

    private static AcerActionFunction ReadActionFunction()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                AcerWmiProtocol.WmiNamespace, $"SELECT * FROM {AcerWmiProtocol.ActionClass}");

            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    using var inParams = instance.GetMethodParameters("GetFunction");
                    inParams["uiInput"] = 0x0007u;
                    using var outParams = instance.InvokeMethod("GetFunction", inParams, null);

                    var raw = outParams?.Properties["uiOutput"]?.Value;
                    if (raw == null) return AcerActionFunction.Unknown;

                    return (byte)((Convert.ToUInt64(raw, CultureInfo.InvariantCulture) >> 16) & 0xFF) switch
                    {
                        1 => AcerActionFunction.CoolBoost,
                        6 => AcerActionFunction.OperatingMode,
                        _ => AcerActionFunction.Unknown
                    };
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Could not read APGeAction function: {ex.Message}", "Capabilities");
        }

        return AcerActionFunction.Unknown;
    }

    private static EcHidInfo? ProbeEcHid()
    {
        try
        {
            return AcerEcHid.Probe();
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Could not probe EC HID interface: {ex.Message}", "Capabilities");
            return null;
        }
    }
}
