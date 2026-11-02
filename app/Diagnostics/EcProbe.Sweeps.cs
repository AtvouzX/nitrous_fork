using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using Nitrous.Enums;
using Nitrous.Managers;

namespace Nitrous.Diagnostics;

public static partial class EcProbe
{
    private static Dictionary<string, object?> CollectSensors(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        WithGamingInstance(instance =>
        {
            foreach (var (name, address, mask) in KnownSensors)
            {
                var raw = TryGetSysInfo(instance, address);
                result[name] = new Dictionary<string, object?>
                {
                    ["address"] = $"0x{address:X4}",
                    ["raw"] = raw,
                    ["decoded"] = raw.HasValue ? (long)((raw.Value >> 8) & mask) : null
                };
            }

            return true;
        }, errors, "sensors");

        return result;
    }

    private static Dictionary<string, object?> CollectMiscSettings(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        WithGamingInstance(instance =>
        {
            for (uint address = 0x00; address <= 0x0F; address++)
            {
                var raw = TryGetMisc(instance, address);
                result[$"0x{address:X2}"] = new Dictionary<string, object?>
                {
                    ["raw"] = raw,
                    ["profileByte"] = raw.HasValue ? (int)((raw.Value >> 8) & 0xFF) : null
                };
            }

            return true;
        }, errors, "miscSettings");

        return result;
    }

    private static Dictionary<string, object?> CollectProfiles(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        WithGamingInstance(instance =>
        {
            var profiles = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; index <= 7; index++)
            {
                profiles[$"{index}"] = TryInvoke(
                    instance, AcerWmiProtocol.GetGamingProfile, "gmInput", index, "gmOutput");
            }

            result["getGamingProfile"] = profiles;

            var supported = TryGetMisc(instance, AcerWmiProtocol.SupportedProfilesSetting);
            result["supportedProfilesRaw"] = supported;
            result["supportedProfilesBitmap"] = supported.HasValue
                ? $"0x{((supported.Value >> 8) & 0xFF):X2}"
                : null;
            result["supportedProfilesNames"] = supported.HasValue
                ? DecodeProfiles((int)((supported.Value >> 8) & 0xFF))
                : Array.Empty<string>();
            result["supportedProfilesUnmappedBits"] = supported.HasValue
                ? UnmappedProfileBits((int)((supported.Value >> 8) & 0xFF))
                : string.Empty;

            var profileSettings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; index <= 0x0F; index++)
            {
                var (value, error) = TryReadRaw(
                    instance, "GetGamingProfileSetting", "gmInput", index, "gmOutput");
                profileSettings[$"0x{index:X2}"] = value;
                if (error != null) profileSettings[$"0x{index:X2}Error"] = error;
            }

            result["getGamingProfileSetting"] = profileSettings;

            var current = TryGetMisc(instance, (uint)AcerWmiProtocol.PowerModeOpcode);
            result["misc0BRaw"] = current;
            result["misc0BValue"] = current.HasValue
                ? DecodeProfile((int)((current.Value >> 8) & 0xFF))
                : null;

            return true;
        }, errors, "profiles");

        return result;
    }

    private static string[] DecodeProfiles(int bitmap) =>
        Enumerable.Range(0, 8)
            .Where(bit => (bitmap & (1 << bit)) != 0)
            .Where(bit => Enum.IsDefined(typeof(PowerProfile), (ulong)bit))
            .Select(DecodeProfile)
            .ToArray();

    private static string UnmappedProfileBits(int bitmap) =>
        string.Join(",", Enumerable.Range(0, 8)
            .Where(bit => (bitmap & (1 << bit)) != 0)
            .Where(bit => !Enum.IsDefined(typeof(PowerProfile), (ulong)bit))
            .Select(bit => $"0x{bit:X}"));

    private static string DecodeProfile(int value)
    {
        var name = Enum.IsDefined(typeof(PowerProfile), (ulong)value)
            ? ((PowerProfile)(ulong)value).ToString()
            : "unknown";
        return $"{name}({value})";
    }

    private static Dictionary<string, object?> CollectGenericFunction(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var className in new[] { AcerWmiProtocol.ActionClass, "AcerGenericMethod" })
        {
            WithInstance(className, instance =>
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                for (uint index = 0; index <= 0x40; index++)
                {
                    var (value, error) = TryReadRaw(
                        instance, "GetFunction", "uiInput", index, "uiOutput");

                    values[$"{index}"] = value;
                    if (error != null) values[$"{index}Error"] = error;
                }

                result[className] = values;
                return true;
            }, errors, $"genericFunction/{className}");
        }

        return result;
    }

    private static void WithGamingInstance(
        Func<ManagementObject, bool> action,
        List<string> errors,
        string section) =>
        WithInstance(AcerWmiProtocol.GamingClass, action, errors, section);

    private static Dictionary<string, object?> CollectKeyboardBacklight(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var capabilities = AcerCapabilitiesProvider.Current;
            result["smbiosKeyboardColor"] = capabilities.Smbios.KeyboardColor;
            result["ecHidPresent"] = capabilities.EcHid != null;
        }
        catch (Exception ex)
        {
            errors.Add($"keyboardBacklight/capabilities: {ex.Message}");
        }

        result["eNekHidPresent"] = false;

        WithGamingInstance(instance =>
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var failures = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            var inputs = Enumerable.Range(0, 0x11).Select(index => (uint)index).Append(0xFFu);
            foreach (var input in inputs)
            {
                var (value, error) = TryReadRaw(
                    instance, "GetGamingKBBacklight", "gmInput", input, "gmOutput");

                values[$"0x{input:X2}"] = value;
                if (error != null) failures[$"0x{input:X2}"] = error;
            }

            result["getGamingKBBacklight"] = values;
            result["getGamingKBBacklightErrors"] = failures;

            try
            {
                result["getGamingKBBacklightOutput"] = DescribeOutput(
                    instance, "GetGamingKBBacklight", "gmInput", 0x01);
            }
            catch (Exception ex)
            {
                result["getGamingKBBacklightOutputError"] = ex.Message;
            }

            return true;
        }, errors, "keyboardBacklight");

        return result;
    }

    private static Dictionary<string, object?> CollectLedState(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        WithGamingInstance(instance =>
        {
            var sweeps = new (string Method, uint[] Inputs)[]
            {
                ("GetGamingLEDBehavior", new uint[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }),
                ("GetGamingLED", new uint[] { 0x00, 0x01, 0x02, 0x03, 0x08, 0x10 }),
                ("GetGamingRgbKb", new uint[] { 0x01, 0x02, 0x04, 0x08, 0x0F })
            };

            foreach (var (method, inputs) in sweeps)
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                foreach (var input in inputs)
                {
                    var (value, error) = TryReadRaw(instance, method, "gmInput", input, "gmOutput");
                    values[$"0x{input:X2}"] = value;
                    if (error != null) values[$"0x{input:X2}Error"] = error;
                }

                result[method] = values;
            }

            return true;
        }, errors, "ledState");

        return result;
    }

    private static Dictionary<string, object?> CollectFans(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        WithGamingInstance(instance =>
        {
            var behavior = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; index <= 3; index++)
            {
                var (value, error) = TryReadRaw(
                    instance, "GetGamingFanBehavior", "gmInput", index, "gmOutput");

                behavior[$"{index}"] = value;
                if (error != null) behavior[$"{index}Error"] = error;
            }

            result["getGamingFanBehavior"] = behavior;

            var speeds = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in new uint[] { 1, 2, 4, 7 })
            {
                var (value, error) = TryReadRaw(
                    instance, "GetGamingFanSpeed", "gmInput", device, "gmOutput");

                speeds[$"0x{device:X}"] = value;
                if (error != null) speeds[$"0x{device:X}Error"] = error;
            }

            result["getGamingFanSpeed"] = speeds;

            result["getGamingFanTable"] = DescribeOutputWith(
                instance, "GetGamingFanTable", Array.Empty<(string Name, object Value)>());

            return true;
        }, errors, "fans");

        return result;
    }

    private static Dictionary<string, object?> CollectBattery(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        WithInstance(AcerWmiProtocol.BatteryClass, instance =>
        {
            result["getBattInfoInterface"] = DescribeOutputWith(
                instance,
                "GetBattInfoInterface",
                new (string Name, object Value)[]
                {
                    ("uBatteryInfoIndex", 0u),
                    ("uBatteryNo", 1u)
                });

            return true;
        }, errors, "battery");

        return result;
    }

    private static Dictionary<string, object?> CollectKeyboardAutoOff(List<string> errors)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var hotkey = ReadAcerHotkeyNumber();
        var input = (uint)((hotkey << 8) | 0x80001);

        result["bkHotkeyNumber"] = hotkey;
        result["readInput"] = $"0x{input:X}";

        WithInstance(AcerWmiProtocol.ActionClass, instance =>
        {
            var (value, error) = TryReadRaw(instance, "GetFunction", "uiInput", input, "uiOutput");
            result["getFunctionRaw"] = value;

            if (error != null)
            {
                result["getFunctionError"] = error;
                return true;
            }

            if (value != null &&
                ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            {
                result["byte4"] = (int)((raw >> 32) & 0xFF);
                result["byte5"] = (int)((raw >> 40) & 0xFF);
                result["byte6"] = (int)((raw >> 48) & 0xFF);
                result["enabled30s"] = ((raw >> 40) & 0xFF) == 0x1E;
            }

            return true;
        }, errors, "keyboardAutoOff");

        return result;
    }

    private static int ReadAcerHotkeyNumber()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\OEM\AcerAgentService");

            if (key?.GetValue("BK_Hotkey_Number") is int value && value > 0 && value <= 0xFF)
                return value;
        }
        catch { }

        return 132;
    }

    private static List<Dictionary<string, object?>> CollectHidDevices(List<string> errors)
    {
        var devices = new List<Dictionary<string, object?>>();

        try
        {
            foreach (var device in AcerEcHid.ListAcerDevices())
            {
                devices.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["vendorId"] = $"0x{device.VendorId:X4}",
                    ["productId"] = $"0x{device.ProductId:X4}",
                    ["usagePage"] = $"0x{device.UsagePage:X4}",
                    ["usage"] = $"0x{device.Usage:X2}",
                    ["featureLength"] = device.FeatureLength
                });
            }
        }
        catch (Exception ex)
        {
            errors.Add($"hidDevices: {ex.Message}");
        }

        return devices;
    }

    private static Dictionary<string, object?> DescribeOutput(
        ManagementObject instance,
        string methodName,
        string inputName,
        uint input)
    {
        var dump = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!IsReadOnlyMethod(methodName))
            throw new InvalidOperationException($"EC probe is read-only; refusing {methodName}.");

        using var inParams = instance.GetMethodParameters(methodName);
        inParams[inputName] = input;

        using var outParams = instance.InvokeMethod(methodName, inParams, null);
        if (outParams == null)
        {
            dump["<no output object>"] = true;
            return dump;
        }

        foreach (PropertyData property in outParams.Properties)
            dump[property.Name] = DescribeValue(property.Value);

        return dump;
    }

    private static (string? Value, string? Error) TryReadRaw(
        ManagementObject instance,
        string methodName,
        string inputName,
        uint input,
        string outputName) =>
        TryReadWith(instance, methodName, new (string Name, object Value)[] { (inputName, input) }, outputName);

    private static (string? Value, string? Error) TryReadWith(
        ManagementObject instance,
        string methodName,
        (string Name, object Value)[] inputs,
        string outputName)
    {
        try
        {
            if (!IsReadOnlyMethod(methodName))
                throw new InvalidOperationException($"EC probe is read-only; refusing {methodName}.");

            using var inParams = instance.GetMethodParameters(methodName);
            foreach (var (name, value) in inputs) inParams[name] = value;

            using var outParams = instance.InvokeMethod(methodName, inParams, null);
            if (outParams == null) return (null, "no output object");

            foreach (PropertyData property in outParams.Properties)
            {
                if (string.Equals(property.Name, outputName, StringComparison.OrdinalIgnoreCase))
                    return (DescribeValue(property.Value), null);
            }

            return (null, $"no {outputName} property");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static Dictionary<string, object?> DescribeOutputWith(
        ManagementObject instance,
        string methodName,
        (string Name, object Value)[] inputs)
    {
        var dump = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!IsReadOnlyMethod(methodName))
                throw new InvalidOperationException($"EC probe is read-only; refusing {methodName}.");

            using var inParams = instance.GetMethodParameters(methodName);
            foreach (var (name, value) in inputs) inParams[name] = value;

            using var outParams = instance.InvokeMethod(methodName, inParams, null);
            if (outParams == null)
            {
                dump["<no output object>"] = true;
                return dump;
            }

            foreach (PropertyData property in outParams.Properties)
                dump[property.Name] = DescribeValue(property.Value);
        }
        catch (Exception ex)
        {
            dump["<error>"] = ex.Message;
        }

        return dump;
    }

    private static string? DescribeValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;

            case byte[] bytes:
                return Convert.ToHexString(bytes);

            case Array array:
                return "[" + string.Join(",", array.Cast<object?>().Select(DescribeValue)) + "]";

            default:
                try
                {
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return value.ToString();
                }
        }
    }

    private static void WithInstance(
        string className,
        Func<ManagementObject, bool> action,
        List<string> errors,
        string section)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                AcerWmiProtocol.WmiNamespace,
                $"SELECT * FROM {className}");

            foreach (ManagementObject instance in searcher.Get())
            {
                using (instance)
                {
                    action(instance);
                    return;
                }
            }

            errors.Add($"{section}: {className} has no instance.");
        }
        catch (Exception ex)
        {
            errors.Add($"{section}: {ex.Message}");
        }
    }

    private static ulong? TryGetSysInfo(ManagementObject instance, uint address) =>
        TryInvoke(instance, AcerWmiProtocol.GetGamingSysInfo, "gmInput", address, "gmOutput");

    private static ulong? TryGetMisc(ManagementObject instance, uint address) =>
        TryInvoke(instance, AcerWmiProtocol.GetGamingMiscSetting, "gmInput", address, "gmOutput");

    private static ulong? TryInvoke(
        ManagementObject instance,
        string methodName,
        string inputName,
        uint input,
        string outputName)
    {
        try
        {
            return InvokeGetLocked(instance, methodName, inputName, input, outputName);
        }
        catch
        {
            return null;
        }
    }

    internal static ulong? InvokeGetLocked(
        ManagementObject instance,
        string methodName,
        string inputName,
        uint input,
        string outputName)
    {
        if (!IsReadOnlyMethod(methodName))
            throw new InvalidOperationException($"EC probe is read-only; refusing {methodName}.");

        using var inParams = instance.GetMethodParameters(methodName);
        inParams[inputName] = input;

        using var outParams = instance.InvokeMethod(methodName, inParams, null);
        if (outParams == null) return null;

        foreach (PropertyData property in outParams.Properties)
        {
            if (string.Equals(property.Name, outputName, StringComparison.OrdinalIgnoreCase))
                return property.Value == null
                    ? null
                    : Convert.ToUInt64(property.Value, CultureInfo.InvariantCulture);
        }

        return null;
    }
}
