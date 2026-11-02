using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Interop;

namespace Nitrous.Managers;

internal sealed record EcHidInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int FeatureLength,
    string? Version,
    ushort? Capability,
    IReadOnlyList<PowerProfile> Modes);

/// <summary>
/// Embedded controller HID feature-report transport (VID 0x1025, PID 0x174B).
/// Authoritative power mode channel on 2024+ Nitro/Predator laptops.
/// </summary>
internal sealed class AcerEcHid : IDisposable
{
    private const int Attempts = 5;
    private static readonly TimeSpan AnswerDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(1);

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;

    private readonly SafeFileHandle _handle;
    private readonly Mutex? _lock;
    private readonly byte[] _reply;

    private AcerEcHid(
        SafeFileHandle handle,
        Mutex? machineLock,
        EcHidInterface info,
        IReadOnlyList<PowerProfile>? knownModes)
    {
        _handle = handle;
        _lock = machineLock;
        _reply = new byte[Math.Max(info.FeatureLength, AcerEcHidProtocol.MinimumReportLength)];

        DevicePath = info.Path;
        VendorId = info.VendorId;
        ProductId = info.ProductId;
        UsagePage = info.UsagePage;
        Usage = info.Usage;
        FeatureLength = info.FeatureLength;

        if (knownModes is { Count: > 0 })
        {
            Modes = knownModes;
        }
        else
        {
            Version = ReadVersion();
            Capability = ReadCapability();
            Modes = AcerEcHidProtocol.ModesFor(Capability);
        }
    }

    public string DevicePath { get; }
    public ushort VendorId { get; }
    public ushort ProductId { get; }
    public ushort UsagePage { get; }
    public ushort Usage { get; }
    public int FeatureLength { get; }
    public string? Version { get; }
    public ushort? Capability { get; }
    public IReadOnlyList<PowerProfile> Modes { get; }

    public static AcerEcHid? Open() => OpenInternal(null);

    public static AcerEcHid? OpenWith(IReadOnlyList<PowerProfile> modes) => OpenInternal(modes);

    public static EcHidInfo? Probe()
    {
        using var hid = OpenInternal(null);
        return hid is null
            ? null
            : new EcHidInfo(
                hid.DevicePath, hid.VendorId, hid.ProductId, hid.UsagePage, hid.Usage,
                hid.FeatureLength, hid.Version, hid.Capability, hid.Modes);
    }

    public sealed record HidSummary(
        ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, int FeatureLength);

    public static IReadOnlyList<HidSummary> ListAcerDevices()
    {
        var devices = new List<HidSummary>();

        foreach (var path in DevicePaths())
        {
            using var handle = OpenDevice(path);
            if (handle == null) continue;

            var info = Describe(handle, path);
            if (info == null) continue;

            if (info.VendorId != AcerEcHidProtocol.ExpectedVendorId && info.VendorId != 0x0CF2) continue;

            devices.Add(new HidSummary(
                info.VendorId, info.ProductId, info.UsagePage, info.Usage, info.FeatureLength));
        }

        return devices;
    }

    private static AcerEcHid? OpenInternal(IReadOnlyList<PowerProfile>? knownModes)
    {
        foreach (var path in DevicePaths())
        {
            var handle = OpenDevice(path);
            if (handle == null) continue;

            var info = Describe(handle, path);
            if (info == null)
            {
                handle.Dispose();
                continue;
            }

            if (info.VendorId != AcerEcHidProtocol.ExpectedVendorId || info.ProductId != AcerEcHidProtocol.ExpectedProductId ||
                info.UsagePage != AcerEcHidProtocol.ExpectedUsagePage || info.Usage != AcerEcHidProtocol.ExpectedUsage)
            {
                handle.Dispose();
                continue;
            }

            return new AcerEcHid(handle, OpenMachineLock(), info, knownModes);
        }

        return null;
    }

    public PowerProfile? ReadMode()
    {
        if (Exchange(AcerEcHidProtocol.CommandMode, AcerEcHidProtocol.FunctionGet) is not { } reply) return null;
        return AcerEcHidProtocol.ModeFromValue(Modes, AcerEcHidProtocol.Byte(reply.Data, 4));
    }

    public bool WriteMode(PowerProfile mode)
    {
        var value = AcerEcHidProtocol.ValueFor(Modes, mode);
        if (value == null) return false;

        return Exchange(AcerEcHidProtocol.CommandMode, AcerEcHidProtocol.FunctionSet, value.Value) is { } reply
               && reply.Status == AcerEcHidProtocol.StatusDone;
    }

    public (int Brightness, int TimeoutSeconds)? ReadBacklightTimeout()
    {
        if (Exchange(AcerEcHidProtocol.CommandDevice, AcerEcHidProtocol.FunctionGet, AcerEcHidProtocol.KeyboardBacklight) is not { } reply)
            return null;

        return AcerEcHidProtocol.DecodeBacklightTimeout(reply.Data);
    }

    public bool WriteBacklightTimeout(int brightness, int timeoutSeconds)
    {
        var data = AcerEcHidProtocol.EncodeBacklightTimeout(brightness, timeoutSeconds);

        return Exchange(AcerEcHidProtocol.CommandDevice, AcerEcHidProtocol.FunctionSet, data) is { } reply
               && reply.Status == AcerEcHidProtocol.StatusDone;
    }

    public bool SetBacklightTimeout(int timeoutSeconds) =>
        ReadBacklightTimeout() is { } current &&
        WriteBacklightTimeout(current.Brightness, timeoutSeconds);

    private string? ReadVersion()
    {
        if (Exchange(AcerEcHidProtocol.CommandStatus, 0) is not { } reply) return null;
        return AcerEcHidProtocol.ParseVersion(reply.Data);
    }

    private ushort? ReadCapability()
    {
        if (Exchange(AcerEcHidProtocol.CommandStatus, AcerEcHidProtocol.StatusModeCapability) is not { } reply) return null;
        return AcerEcHidProtocol.ParseCapability(reply.Data, Version);
    }

    private AcerEcHidProtocol.Reply? Exchange(ushort command, byte function, params byte[] data)
    {
        var request = AcerEcHidProtocol.BuildRequest(command, function, data, FeatureLength);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RetryDelay);

            var locked = false;
            try
            {
                locked = _lock?.WaitOne(LockTimeout) ?? false;
                if (!Hid.HidD_SetFeature(_handle, request, request.Length)) continue;

                Thread.Sleep(AnswerDelay);
                Array.Clear(_reply);
                _reply[0] = AcerEcHidProtocol.ReportId;
                if (!Hid.HidD_GetFeature(_handle, _reply, _reply.Length)) continue;

                if (AcerEcHidProtocol.DecodeReply(_reply) is not { } reply) continue;
                if (reply.Command != command) continue;
                if (reply.Status != AcerEcHidProtocol.StatusDone && reply.Status != AcerEcHidProtocol.StatusUnsupported) continue;
                return reply;
            }
            catch (AbandonedMutexException)
            {
                locked = true;
            }
            finally
            {
                if (locked) _lock!.ReleaseMutex();
            }
        }

        return null;
    }

    private static IReadOnlyList<string> DevicePaths()
    {
        Guid guid;
        Hid.HidD_GetHidGuid(out guid);
        int length = 0;
        if (Cfgmgr32.CM_Get_Device_Interface_List_SizeW(out length, ref guid, null, 0) != Cfgmgr32.CR_SUCCESS || length <= 0)
            return Array.Empty<string>();

        char[] buffer = new char[length];
        int result = Cfgmgr32.CM_Get_Device_Interface_ListW(ref guid, null, buffer, length, 0);
        if (result != Cfgmgr32.CR_SUCCESS)
            return Array.Empty<string>();

        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static SafeFileHandle? OpenDevice(string path)
    {
        var handle = Kernel32.CreateFileW(
            path, GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return handle;
    }

    private static EcHidInterface? Describe(SafeFileHandle handle, string path)
    {
        var attributes = new Hid.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Hid.HIDD_ATTRIBUTES>() };
        if (!Hid.HidD_GetAttributes(handle, ref attributes)) return null;

        if (!Hid.HidD_GetPreparsedData(handle, out var preparsed)) return null;

        var caps = new Hid.HIDP_CAPS();
        try
        {
            if (Hid.HidP_GetCaps(preparsed, ref caps) < 0) return null;
        }
        finally
        {
            Hid.HidD_FreePreparsedData(preparsed);
        }

        return new EcHidInterface(
            path,
            attributes.VendorId,
            attributes.ProductId,
            caps.UsagePage,
            caps.Usage,
            caps.FeatureReportByteLength);
    }

    private static Mutex? OpenMachineLock()
    {
        try
        {
            return Mutex.TryOpenExisting(AcerEcHidProtocol.MachineLockName, out var existing)
                ? existing
                : new Mutex(false, AcerEcHidProtocol.MachineLockName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
        _lock?.Dispose();
    }

    private sealed record EcHidInterface(
        string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, int FeatureLength);
}
