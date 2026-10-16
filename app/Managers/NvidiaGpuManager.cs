using System;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;
using Nitrous.Enums;

namespace Nitrous.Managers;

public partial class NvidiaGpuManager : IDisposable
{
    private PhysicalGPU? _internalGpu;
    private int _isAutoApplyingClocks;
    public bool IsValid => _internalGpu != null || (IsGpuAwake() && EnsureInitialized());

    public void ResetGpuHandle()
    {
        _internalGpu = null;
        _nvmlDeviceHandle = IntPtr.Zero;
        try { NVIDIA.Unload(); } catch { }
        try { NativeNvml.Shutdown(); } catch { }
    }

    public static bool ShouldAutoApplyGpuClocks(bool wasGpuAwake, bool isGpuAwake, bool isAcOnline, int autoApplySetting)
    {
        return !wasGpuAwake && isGpuAwake && isAcOnline && autoApplySetting == 1;
    }

    public static bool ShouldResetGpuHandle(bool wasGpuAwake, bool isGpuAwake)
    {
        return wasGpuAwake && !isGpuAwake;
    }

    public bool EnsureInitialized()
    {
        if (!IsGpuAwake())
        {
            if (_internalGpu != null)
            {
                ResetGpuHandle();
            }
            return false;
        }

        if (_internalGpu != null) return true;
        InitializeNvAPI();
        return _internalGpu != null;
    }

    public int MaxCoreOffset = 250;
    public int MinCoreOffset = -250;
    public int MaxMemoryOffset = 1000;
    public int MinMemoryOffset = -1000;

    // Persist the NVML device handle for rapid polling
    private IntPtr _nvmlDeviceHandle = IntPtr.Zero;
    private double _memoryClockDivisor = 4.0; // Default to GDDR5/6/6X
    private string _cachedArchName = "Unknown";
    private string? _cachedGpuName;
    private int _cachedVramTotal;
    private NvmlProcessInfo[]? _cachedGraphicsInfos;
    private NvmlProcessInfo[]? _cachedComputeInfos;
    private DateTime _sleepPendingUntil = DateTime.MinValue;

    private static string FormatPState(NvmlPstates pState)
    {
        return pState switch
        {
            NvmlPstates.Pstate0 => "P0",
            NvmlPstates.Pstate1 => "P1",
            NvmlPstates.Pstate2 => "P2",
            NvmlPstates.Pstate3 => "P3",
            NvmlPstates.Pstate4 => "P4",
            NvmlPstates.Pstate5 => "P5",
            NvmlPstates.Pstate6 => "P6",
            NvmlPstates.Pstate7 => "P7",
            NvmlPstates.Pstate8 => "P8",
            NvmlPstates.Pstate9 => "P9",
            NvmlPstates.Pstate10 => "P10",
            NvmlPstates.Pstate11 => "P11",
            NvmlPstates.Pstate12 => "P12",
            NvmlPstates.Pstate13 => "P13",
            NvmlPstates.Pstate14 => "P14",
            NvmlPstates.Pstate15 => "P15",
            _ => "Unknown"
        };
    }

    public NvidiaGpuManager()
    {
        InitializeNvAPI();
    }

    private void InitializeNvAPI()
    {
        // Guard against binding unmanaged GPU driver handles in test host environments
        string procName = Process.GetCurrentProcess().ProcessName;
        if (procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
            procName.Contains("vstest", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Guard against waking sleeping dGPU in D3Cold sleep
        if (!IsGpuAwake())
        {
            return;
        }

        try
        {
            try { NVIDIA.Unload(); } catch { }
            try { NativeNvml.Shutdown(); } catch { }

            // Initialize NvAPI for Overclocking
            NVIDIA.Initialize();
            _internalGpu = GetInternalDiscreteGpu();

            // Initialize NVML for Telemetry
            if (NativeNvml.Init() == NvmlReturn.Success)
            {
                // Grab the handle for the primary GPU (index 0)
                NativeNvml.DeviceGetHandleByIndex(0, out _nvmlDeviceHandle);

                // Fetch architecture to set the correct memory divisor for GDDR7 (Blackwell+)
                if (NativeNvml.DeviceGetArchitecture(_nvmlDeviceHandle, out NvmlDeviceArchitecture arch) == NvmlReturn.Success)
                {
                    _memoryClockDivisor = ((int)arch >= 10) ? 8.0 : 4.0;
                    _cachedArchName = arch.ToString();
                }
            }
        }
        catch
        {
            _internalGpu = null;
            _nvmlDeviceHandle = IntPtr.Zero;
        }
    }

    private static PhysicalGPU? GetInternalDiscreteGpu()
    {
        try
        {
            return PhysicalGPU
                .GetPhysicalGPUs()
                .FirstOrDefault(gpu => gpu.SystemType == SystemType.Laptop)
                ?? PhysicalGPU.GetPhysicalGPUs().FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public bool GetClocks(out int core, out int memory)
    {
        core = memory = 0;
        if (!EnsureInitialized()) return false;

        try
        {
            IPerformanceStates20Info states = GPUApi.GetPerformanceStates20(_internalGpu!.Handle);
            var p0Clocks = states.Clocks[PerformanceStateId.P0_3DPerformance];

            var coreClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics);
            var memClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory);

            core = (coreClock?.FrequencyDeltaInkHz.DeltaValue ?? 0) / 1000;
            memory = (memClock?.FrequencyDeltaInkHz.DeltaValue ?? 0) / 1000;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public int SetClocks(int core, int memory) => SetClocksInternal(core, memory);

    private int SetClocksInternal(int core, int memory)
    {
        if (!EnsureInitialized()) return 0;

        if (core < MinCoreOffset || core > MaxCoreOffset) return 0;
        if (memory < MinMemoryOffset || memory > MaxMemoryOffset) return 0;

        GetClocks(out int currentCore, out int currentMemory);

        if (Math.Abs(core - currentCore) < 5 && Math.Abs(memory - currentMemory) < 5)
            return 1;

        var coreClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(core * 1000));
        var memoryClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memory * 1000));

        PerformanceStates20ClockEntryV1[] clocks = { coreClock, memoryClock };
        PerformanceStates20BaseVoltageEntryV1[] voltages = { };

        PerformanceStates20InfoV1.PerformanceState20[] performanceStates = {
                new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clocks, voltages)
            };

        var overclock = new PerformanceStates20InfoV1(performanceStates, 2, 0);

        try
        {
            GPUApi.SetPerformanceStates20(_internalGpu!.Handle, overclock);
            LogManager.Info($"Applied GPU offsets: Core={core} MHz, Memory={memory} MHz", "NvidiaGpu");
            return 1;
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed to apply GPU offsets: {ex.Message}", "NvidiaGpu");
            ResetGpuHandle();
            return 0;
        }
    }

    public static bool IsAcPowerConnected()
    {
        try
        {
            return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        }
        catch
        {
            return true;
        }
    }

    public async Task<int> AutoApplyClocksAsync(PowerProfile? activeProfile = null, bool? isAcOnline = null)
    {
        bool online = isAcOnline ?? IsAcPowerConnected();
        if (!online || !IsGpuAwake())
        {
            LogManager.Debug("AutoApplyClocksAsync: dGPU is on battery or asleep; skipping auto-apply.", "NvidiaGpu");
            return 0;
        }

        if (SettingsManager.Get("AutoApplyGpuClocks", 1) != 1)
        {
            LogManager.Debug("AutoApplyClocksAsync: AutoApplyGpuClocks disabled in settings.", "NvidiaGpu");
            return 0;
        }

        if (Interlocked.CompareExchange(ref _isAutoApplyingClocks, 1, 0) != 0)
        {
            return 0;
        }

        try
        {
            var profile = activeProfile ?? (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);
            int core = SettingsManager.Get($"GpuCore_{profile}", GetDefaultCore(profile));
            int mem = SettingsManager.Get($"GpuMemory_{profile}", GetDefaultMemory(profile));

            if (core == 0 && mem == 0) return 0;

            const int maxAttempts = 5;
            const int delayMs = 1000;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (!IsGpuAwake() || !(isAcOnline ?? IsAcPowerConnected())) return 0;

                try
                {
                    if (EnsureInitialized())
                    {
                        int result = SetClocksInternal(core, mem);
                        if (result == 1)
                        {
                            LogManager.Info($"Auto-applied GPU clocks: Core={core} MHz, Memory={mem} MHz (Profile: {profile})", "NvidiaGpu");
                            return 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Debug($"AutoApplyClocksAsync attempt {attempt} failed: {ex.Message}", "NvidiaGpu");
                }

                if (attempt < maxAttempts)
                {
                    ResetGpuHandle();
                    await Task.Delay(delayMs);
                }
            }

            return 0;
        }
        finally
        {
            Interlocked.Exchange(ref _isAutoApplyingClocks, 0);
        }
    }

    public async Task ApplyOnBootAsync(PowerProfile currentProfile, bool? isAcOnline = null)
    {
        await AutoApplyClocksAsync(currentProfile, isAcOnline);
    }

    public async Task<int> ApplyPowerProfileOcAsync(PowerProfile profile, bool? isAcOnline = null)
    {
        bool online = isAcOnline ?? IsAcPowerConnected();
        if (!online || !IsGpuAwake())
        {
            // Core Battery Invariant: discrete NVIDIA GPU must remain in D3Cold sleep on battery.
            // Never invoke NvAPI when on DC power or when the dGPU is asleep.
            LogManager.Debug($"ApplyPowerProfileOcAsync: dGPU is on battery or asleep; skipping OC for {profile}.", "NvidiaGpu");
            return 0;
        }

        return await Task.Run(() =>
        {
            int coreOffset = SettingsManager.Get($"GpuCore_{profile}", GetDefaultCore(profile));
            int memoryOffset = SettingsManager.Get($"GpuMemory_{profile}", GetDefaultMemory(profile));

            return SetClocksInternal(coreOffset, memoryOffset);
        });
    }

    // Helpers to track default values per profile
    public int GetDefaultCore(PowerProfile profile) => profile switch
    {
        PowerProfile.Quiet => -100,
        PowerProfile.Performance => 100,
        PowerProfile.Turbo => 150,
        _ => 0
    };

    public int GetDefaultMemory(PowerProfile profile) => profile switch
    {
        PowerProfile.Quiet => -200,
        PowerProfile.Performance => 150,
        PowerProfile.Turbo => 300,
        _ => 0
    };

    // Saves the user's custom slider values to the active profile
    public void SaveCustomProfileOc(PowerProfile profile, int core, int memory)
    {
        SettingsManager.Save($"GpuCore_{profile}", core);
        SettingsManager.Save($"GpuMemory_{profile}", memory);
    }

    // Overwrites the custom save with defaults
    public async Task ResetProfileToDefaultsAsync(PowerProfile profile)
    {
        SettingsManager.Save($"GpuCore_{profile}", GetDefaultCore(profile));
        SettingsManager.Save($"GpuMemory_{profile}", GetDefaultMemory(profile));
        await ApplyPowerProfileOcAsync(profile);
    }

    public void Dispose()
    {
        try
        {
            ResetGpuHandle();
        }
        catch (Exception ex)
        {
            LogManager.Debug($"Error during NvidiaGpuManager disposal: {ex.Message}", "NvidiaGpu");
        }
    }

    public class GpuTelemetry
    {
        public string Name { get; set; } = "NVIDIA GPU";
        public int CoreTemp { get; set; }
        public int GpuLoad { get; set; }
        public int VramUsedMb { get; set; }
        public int VramTotalMb { get; set; }
        public string PState { get; set; } = "Unknown";
        public int CurrentCoreClock { get; set; }
        public int CurrentMemoryClock { get; set; }
        public double PowerDrawW { get; set; }
        public double MinPowerLimitW { get; set; }
        public double MaxPowerLimitW { get; set; }
        public double EnforcedPowerLimitW { get; set; }
        public string Architecture { get; set; } = "Unknown";
    }

    // Cached PNP instance ID for the NVIDIA dGPU, resolved once on first call.
    private static string? _nvidiaInstanceId;
    private static bool _instanceIdResolved;

    /// <summary>
    /// Determines if the discrete NVIDIA GPU is powered on (D0) by reading its
    /// device power state from the Windows Configuration Manager API.
    /// This reads from Windows' own records — it never touches the GPU driver,
    /// so it cannot wake the GPU from D3Cold sleep.
    /// Matches the technique used by OpenSense (GpuPowerState.cs) and the
    /// NVIDIA GPU Activity tray icon.
    /// </summary>
    public static bool IsGpuAwake()
    {
        try
        {
            // Resolve the PNP instance ID once
            if (!_instanceIdResolved)
            {
                _nvidiaInstanceId = ResolveNvidiaInstanceId();
                _instanceIdResolved = true;
            }

            if (_nvidiaInstanceId == null)
                return false;

            // Locate the device node by its PNP instance ID
            int cr = NativeCM.CM_Locate_DevNode(out uint devInst, _nvidiaInstanceId, NativeCM.CM_LOCATE_DEVNODE_NORMAL);
            if (cr != NativeCM.CR_SUCCESS)
                return false;

            // Read the CM_POWER_DATA structure from DEVPKEY_Device_PowerData
            // The PD_MostRecentPowerState field tells us the actual D-state.
            var powerData = new NativeCM.CM_POWER_DATA();
            uint dataSize = (uint)Marshal.SizeOf<NativeCM.CM_POWER_DATA>();
            uint propType = 0;

            cr = NativeCM.CM_Get_DevNode_Property(
                devInst,
                ref NativeCM.DEVPKEY_Device_PowerData,
                out propType,
                ref powerData,
                ref dataSize,
                0);

            if (cr != NativeCM.CR_SUCCESS)
                return false;

            // PowerDeviceD0 = 1 (fully powered / active)
            // PowerDeviceD1/D2/D3 = 2/3/4 (various sleep states including D3Cold)
            return powerData.PD_MostRecentPowerState == NativeCM.PowerDeviceD0;
        }
        catch (Exception ex)
        {
            LogManager.Warn($"IsGpuAwake CM API failed: {ex.Message}", "NvidiaGpu");
        }

        // Ultimate fallback — assume awake so telemetry is never permanently blocked
        return true;
    }

    /// <summary>
    /// Finds the PNP instance ID of the first NVIDIA PCI display adapter.
    /// Uses CM_Get_Device_ID_List filtered to the Display device class.
    /// </summary>
    private static string? ResolveNvidiaInstanceId()
    {
        try
        {
            // GUID_DEVCLASS_DISPLAY = {4D36E968-E325-11CE-BFC1-08002BE10318}
            string filter = "{4D36E968-E325-11CE-BFC1-08002BE10318}";

            int cr = NativeCM.CM_Get_Device_ID_List_Size(out uint listSize, filter,
                NativeCM.CM_GETIDLIST_FILTER_CLASS | NativeCM.CM_GETIDLIST_FILTER_PRESENT);
            if (cr != NativeCM.CR_SUCCESS || listSize == 0)
                return null;

            char[] buffer = new char[listSize];
            cr = NativeCM.CM_Get_Device_ID_List(filter, buffer, listSize,
                NativeCM.CM_GETIDLIST_FILTER_CLASS | NativeCM.CM_GETIDLIST_FILTER_PRESENT);
            if (cr != NativeCM.CR_SUCCESS)
                return null;

            // The buffer is a multi-string (null-separated, double-null terminated)
            string allIds = new string(buffer);
            string[] ids = allIds.Split('\0', StringSplitOptions.RemoveEmptyEntries);

            // Find the NVIDIA GPU by its PCI vendor ID (VEN_10DE)
            return ids.FirstOrDefault(id =>
                id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            LogManager.Warn($"ResolveNvidiaInstanceId failed: {ex.Message}", "NvidiaGpu");
            return null;
        }
    }

    public async Task<GpuTelemetry> GetNvmlTelemetryAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var t = new GpuTelemetry { Architecture = _cachedArchName };

            // Phase 1: Native Sleep Check wrapper
            // If the GPU is asleep, bypass NVML completely to preserve D3Cold power state.
            if (!IsGpuAwake())
            {
                _sleepPendingUntil = DateTime.MinValue;
                t.PState = "Sleep";
                t.CoreTemp = 0;
                t.GpuLoad = 0;
                t.Name = "NVIDIA (Asleep)";
                return t;
            }

            // If we recently unloaded NVML to allow D3Cold sleep, do not re-init NVML immediately
            // while the driver is transitioning to sleep.
            if (DateTime.UtcNow < _sleepPendingUntil)
            {
                t.PState = "Sleep";
                t.CoreTemp = 0;
                t.GpuLoad = 0;
                t.Name = "NVIDIA (Asleep)";
                return t;
            }

            // If the application started while the GPU was asleep, the NVML handle might be zero.
            // Now that we know it is awake, initialize the handle if it's missing.
            if (_nvmlDeviceHandle == IntPtr.Zero)
            {
                if (NativeNvml.Init() == NvmlReturn.Success)
                {
                    NativeNvml.DeviceGetHandleByIndex(0, out _nvmlDeviceHandle);
                    if (NativeNvml.DeviceGetArchitecture(_nvmlDeviceHandle, out NvmlDeviceArchitecture arch) == NvmlReturn.Success)
                    {
                        _memoryClockDivisor = ((int)arch >= 10) ? 8.0 : 4.0;
                        _cachedArchName = arch.ToString();
                        t.Architecture = _cachedArchName;
                    }
                }
            }

            if (_nvmlDeviceHandle == IntPtr.Zero) return t;

            try
            {
                // Phase 2: Check if Nitrous itself is the only process keeping the GPU awake.
                // If so, shut down NVML and NvAPI to allow the GPU to go back to D3Cold sleep.
                uint graphicsCount = 32;
                _cachedGraphicsInfos ??= new NvmlProcessInfo[32];
                bool hasGraphics = NativeNvml.DeviceGetGraphicsRunningProcesses(_nvmlDeviceHandle, ref graphicsCount, _cachedGraphicsInfos) == NvmlReturn.Success;

                uint computeCount = 32;
                _cachedComputeInfos ??= new NvmlProcessInfo[32];
                bool hasCompute = NativeNvml.DeviceGetComputeRunningProcesses(_nvmlDeviceHandle, ref computeCount, _cachedComputeInfos) == NvmlReturn.Success;

                if (hasGraphics || hasCompute)
                {
                    bool shouldSleep = IsOnlyNitrousRunningOnGpu(
                        hasGraphics ? _cachedGraphicsInfos : null, hasGraphics ? graphicsCount : 0,
                        hasCompute ? _cachedComputeInfos : null, hasCompute ? computeCount : 0,
                        Environment.ProcessId);

                    if (shouldSleep)
                    {
                        LogManager.Info("Only Nitrous is running on the GPU. Unloading NVML to allow sleep.", "NvidiaGpu");
                        try { NVIDIA.Unload(); } catch { }
                        try { NativeNvml.Shutdown(); } catch { }
                        
                        _nvmlDeviceHandle = IntPtr.Zero;
                        _internalGpu = null;
                        _sleepPendingUntil = DateTime.UtcNow.AddSeconds(15);

                        t.PState = "Sleep";
                        t.CoreTemp = 0;
                        t.GpuLoad = 0;
                        t.Name = "NVIDIA (Asleep)";
                        return t;
                    }
                }
            }
            catch
            {
                // Ignore process check errors on older drivers
            }

            try
            {
                // Name
                if (_cachedGpuName == null)
                {
                    var nameBuilder = new StringBuilder(64);
                    if (NativeNvml.DeviceGetName(_nvmlDeviceHandle, nameBuilder, (uint)nameBuilder.Capacity) == NvmlReturn.Success)
                        _cachedGpuName = nameBuilder.ToString();
                }
                t.Name = _cachedGpuName ?? "NVIDIA GPU";

                // Load
                if (NativeNvml.DeviceGetUtilizationRates(_nvmlDeviceHandle, out NvmlUtilization util) == NvmlReturn.Success)
                    t.GpuLoad = (int)util.Gpu;

                // Temp
                if (NativeNvml.DeviceGetTemperature(_nvmlDeviceHandle, NvmlTemperatureSensors.Gpu, out uint temp) == NvmlReturn.Success)
                    t.CoreTemp = (int)temp;

                // Memory
                if (NativeNvml.DeviceGetMemoryInfo(_nvmlDeviceHandle, out NvmlMemory mem) == NvmlReturn.Success)
                {
                    // Convert bytes to Megabytes
                    t.VramUsedMb = (int)(mem.Used / (1024 * 1024));
                    if (_cachedVramTotal == 0)
                        _cachedVramTotal = (int)(mem.Total / (1024 * 1024));
                    t.VramTotalMb = _cachedVramTotal;
                }

                // P-State (Format string to match SMI output like "P0", "P8")
                if (NativeNvml.DeviceGetPerformanceState(_nvmlDeviceHandle, out NvmlPstates pState) == NvmlReturn.Success)
                    t.PState = FormatPState(pState);

                // Clocks
                if (NativeNvml.DeviceGetClockInfo(_nvmlDeviceHandle, NvmlClockType.Graphics, out uint coreClock) == NvmlReturn.Success)
                    t.CurrentCoreClock = (int)coreClock;

                if (NativeNvml.DeviceGetClockInfo(_nvmlDeviceHandle, NvmlClockType.Mem, out uint memClock) == NvmlReturn.Success)
                    t.CurrentMemoryClock = (int)(memClock / _memoryClockDivisor);

                // Power Limits (Convert milliwatts to Watts)
                if (NativeNvml.DeviceGetPowerUsage(_nvmlDeviceHandle, out uint powerDraw) == NvmlReturn.Success)
                    t.PowerDrawW = Math.Round(powerDraw / 1000.0, 1);

                if (NativeNvml.DeviceGetEnforcedPowerLimit(_nvmlDeviceHandle, out uint enforced) == NvmlReturn.Success)
                    t.EnforcedPowerLimitW = Math.Round(enforced / 1000.0, 1);

                if (NativeNvml.DeviceGetPowerManagementLimitConstraints(_nvmlDeviceHandle, out uint minLimit, out uint maxLimit) == NvmlReturn.Success)
                {
                    t.MinPowerLimitW = Math.Round(minLimit / 1000.0, 1);
                    t.MaxPowerLimitW = Math.Round(maxLimit / 1000.0, 1);
                }
            }
            catch (Exception ex)
            {
                // Ignore P/Invoke exceptions on unsupported platforms or sleeping GPUs
                LogManager.Debug($"GPU telemetry read failed: {ex.Message}", "NvidiaGpu");
            }

            return t;
        }, cancellationToken);
    }

    /// <summary>
    /// Pure function to evaluate if Nitrous is the solitary process keeping the GPU awake.
    /// Extracted for unit testability without relying on NVML bindings.
    /// </summary>
    public static bool IsOnlyNitrousRunningOnGpu(
        NvmlProcessInfo[]? graphicsInfos, uint graphicsCount,
        NvmlProcessInfo[]? computeInfos, uint computeCount, 
        int currentPid)
    {
        bool onlyNitrous = true;
        bool hasAnyProcess = false;

        if (graphicsInfos != null && graphicsCount > 0)
        {
            for (int i = 0; i < graphicsCount; i++)
            {
                hasAnyProcess = true;
                if (graphicsInfos[i].Pid != currentPid)
                {
                    onlyNitrous = false;
                    break;
                }
            }
        }

        if (computeInfos != null && computeCount > 0 && onlyNitrous)
        {
            for (int i = 0; i < computeCount; i++)
            {
                hasAnyProcess = true;
                if (computeInfos[i].Pid != currentPid)
                {
                    onlyNitrous = false;
                    break;
                }
            }
        }

        return hasAnyProcess && onlyNitrous;
    }

}
