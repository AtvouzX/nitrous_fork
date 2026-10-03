using System;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

public class NvidiaGpuManager : IDisposable
{
    private PhysicalGPU? _internalGpu;
    public bool IsValid => _internalGpu != null;

    public int MaxCoreOffset = 250;
    public int MinCoreOffset = -250;
    public int MaxMemoryOffset = 1000;
    public int MinMemoryOffset = -1000;

    public NvidiaGpuManager()
    {
        InitializeNvAPI();
    }

    private void InitializeNvAPI()
    {
        try
        {
            try { NVIDIA.Unload(); } catch { }

            NVIDIA.Initialize();
            _internalGpu = GetInternalDiscreteGpu();
        }
        catch
        {
            _internalGpu = null;
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
        if (!IsValid) return false;

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
        if (!IsValid) return 0;

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
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    public async Task ApplyOnBootAsync(PowerProfile currentProfile)
    {
        int core = SettingsManager.Get($"GpuCore_{currentProfile}", GetDefaultCore(currentProfile));
        int memory = SettingsManager.Get($"GpuMemory_{currentProfile}", GetDefaultMemory(currentProfile));

        if (core == 0 && memory == 0) return;

        const int maxAttempts = 8;
        const int delayMs = 1500;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (_internalGpu == null) InitializeNvAPI();

                if (IsValid)
                {
                    int result = SetClocksInternal(core, memory);
                    if (result == 1) return;
                }
            }
            catch { }

            await Task.Delay(delayMs);
        }
    }

    public async Task<int> ApplyPowerProfileOcAsync(PowerProfile profile)
    {
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
            NVIDIA.Unload();
        }
        catch { }
    }

    public class GpuTelemetry
    {
        public string Name { get; set; } = "Unknown";
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
    }

    private static readonly string CachedSmiPath = FindNvidiaSmiPath();

    private static string FindNvidiaSmiPath()
    {
        string system32Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        if (File.Exists(system32Path)) return system32Path;

        string programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe");
        if (File.Exists(programFilesPath)) return programFilesPath;

        return "nvidia-smi";
    }

    public static async Task<GpuTelemetry> GetSmiTelemetryAsync(CancellationToken cancellationToken = default)
    {
        var t = new GpuTelemetry();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = CachedSmiPath,
                Arguments = "--query-gpu=gpu_name,temperature.gpu,utilization.gpu,memory.used,memory.total,pstate,clocks.current.graphics,clocks.current.memory,power.draw,power.min_limit,power.max_limit,enforced.power.limit --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return t;

            // Combine cancellation token with a 1.5-second timeout for dGPU sleep/D3Cold states
            using var timeoutCts = new CancellationTokenSource(1500);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            string output;
            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(linkedCts.Token);
                output = (await stdoutTask).Trim();
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                return t;
            }

            if (string.IsNullOrWhiteSpace(output)) return t;

            string[] values = output.Split(',');

            if (values.Length >= 12)
            {
                t.Name = values[0].Trim();

                if (int.TryParse(values[1].Trim(), out int temp)) t.CoreTemp = temp;
                if (int.TryParse(values[2].Trim(), out int load)) t.GpuLoad = load;
                if (int.TryParse(values[3].Trim(), out int vramUsed)) t.VramUsedMb = vramUsed;
                if (int.TryParse(values[4].Trim(), out int vramTotal)) t.VramTotalMb = vramTotal;

                t.PState = values[5].Trim();

                if (int.TryParse(values[6].Trim(), out int coreClock)) t.CurrentCoreClock = coreClock;
                if (int.TryParse(values[7].Trim(), out int memClock)) t.CurrentMemoryClock = memClock;

                if (double.TryParse(values[8].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double draw))
                    t.PowerDrawW = draw;

                if (double.TryParse(values[9].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double minLimit))
                    t.MinPowerLimitW = minLimit;

                if (double.TryParse(values[10].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double maxLimit))
                    t.MaxPowerLimitW = maxLimit;

                if (double.TryParse(values[11].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double enforcedLimit))
                    t.EnforcedPowerLimitW = enforcedLimit;
            }
        }
        catch { }

        return t;
    }
}
