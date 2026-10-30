using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class ArchitectureAndSecurityTests
{
    [Fact]
    public void AllPInvokeDeclarations_MustEnforceSystem32SearchPath()
    {
        // Scan all types in the Nitrous application assembly
        var assembly = typeof(UpdateManager).Assembly;
        var violations = new List<string>();

        var types = assembly.GetTypes();
        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            foreach (var m in methods)
            {
                var dllImportAttr = m.GetCustomAttribute<DllImportAttribute>();
                if (dllImportAttr != null)
                {
                    var searchPathAttr = m.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()
                                         ?? type.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();

                    if (searchPathAttr == null || !searchPathAttr.Paths.HasFlag(DllImportSearchPath.System32))
                    {
                        violations.Add($"{type.Name}.{m.Name} importing '{dllImportAttr.Value}'");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} P/Invoke methods missing [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void AssemblyVersion_MatchesUpdateManagerCurrentVersion()
    {
        var assembly = typeof(UpdateManager).Assembly;
        var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();

        Assert.NotNull(fileVersionAttr);

        // e.g. "0.8.0.0" -> "0.8.0"
        var fileVersion = fileVersionAttr.Version;
        var parts = fileVersion.Split('.');
        string threePartVersion = $"{parts[0]}.{parts[1]}.{parts[2]}";

        Assert.Equal(UpdateManager.CurrentVersion, threePartVersion);
    }

    [Fact]
    public void StartupManager_SchtasksPath_IsAbsoluteAndPointsToSystemDirectory()
    {
        string path = StartupManager.CachedSchtasksPath;

        Assert.True(Path.IsPathRooted(path), "schtasks path must be absolute and rooted.");
        Assert.StartsWith(Environment.SystemDirectory, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("schtasks.exe", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), $"schtasks.exe must exist at {path}.");
    }

    [Fact]
    public void UpdateManager_CmdPath_IsAbsoluteAndPointsToSystemDirectory()
    {
        string path = UpdateManager.CachedCmdPath;

        Assert.True(Path.IsPathRooted(path), "cmd.exe path must be absolute and rooted.");
        Assert.StartsWith(Environment.SystemDirectory, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("cmd.exe", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), $"cmd.exe must exist at {path}.");
    }

    [Fact]
    public void CpuPowerManager_PowerCfgPath_IsAbsoluteAndPointsToSystemDirectory()
    {
        string path = CpuPowerManager.CachedPowerCfgPath;

        Assert.True(Path.IsPathRooted(path), "powercfg.exe path must be absolute and rooted.");
        Assert.StartsWith(Environment.SystemDirectory, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("powercfg.exe", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), $"powercfg.exe must exist at {path}.");
    }

    [Theory]
    [InlineData("schtasks.exe")]
    [InlineData("powercfg.exe")]
    [InlineData("cmd.exe")]
    public void AllExternalBinaryPaths_MustBeRootedInSystemDirectory(string binaryName)
    {
        string path = binaryName switch
        {
            "schtasks.exe" => StartupManager.CachedSchtasksPath,
            "powercfg.exe" => CpuPowerManager.CachedPowerCfgPath,
            "cmd.exe" => UpdateManager.CachedCmdPath,
            _ => throw new ArgumentException($"Unknown binary: {binaryName}")
        };

        Assert.True(Path.IsPathRooted(path), $"{binaryName} path must be absolute and rooted.");
        Assert.StartsWith(Environment.SystemDirectory, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(binaryName, path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), $"{binaryName} must exist at {path}.");
    }

    [Fact]
    public void ExplorerBinaryPath_PointsToWindowsDirectoryAndExists()
    {
        string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.True(File.Exists(explorerPath), $"explorer.exe must exist at {explorerPath}");
    }

    [Fact]
    public void Display_DevModeW_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DEVMODEW>();
        Assert.Equal(220, size);
    }

    [Fact]
    public void Powrprof_SystemBatteryState_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.Powrprof.SYSTEM_BATTERY_STATE>();
        Assert.Equal(32, size);
    }

    [Fact]
    public void User32_KbdllHookStruct_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.KBDLLHOOKSTRUCT>();
        Assert.Equal(24, size);
    }

    [Fact]
    public void Cfgmgr32_DevPropKey_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.Cfgmgr32.DEVPROPKEY>();
        Assert.Equal(20, size);
    }

    [Fact]
    public void Cfgmgr32_CmPowerData_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.Cfgmgr32.CM_POWER_DATA>();
        Assert.Equal(56, size);
    }

    [Fact]
    public void Display_DisplayConfigPathInfo_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_PATH_INFO>();
        Assert.Equal(72, size);
    }

    [Fact]
    public void Display_DisplayConfigModeInfo_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_MODE_INFO>();
        Assert.Equal(64, size);
    }

    [Fact]
    public void MemoryHelper_ContainsNoDllImportDeclarations()
    {
        var methods = typeof(Nitrous.Helpers.MemoryHelper)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        var pInvokes = methods.Where(m => m.GetCustomAttribute<DllImportAttribute>() != null).ToList();

        Assert.Empty(pInvokes);
    }

    [Fact]
    public void Display_Luid_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.LUID>();
        Assert.Equal(8, size);
    }

    [Fact]
    public void Display_DisplayConfigRational_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_RATIONAL>();
        Assert.Equal(8, size);
    }

    [Fact]
    public void Display_DisplayConfigPathSourceInfo_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_PATH_SOURCE_INFO>();
        Assert.Equal(20, size);
    }

    [Fact]
    public void Display_DisplayConfigPathTargetInfo_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_PATH_TARGET_INFO>();
        Assert.Equal(48, size);
    }

    [Fact]
    public void Display_DisplayConfig2DRegion_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_2DREGION>();
        Assert.Equal(8, size);
    }

    [Fact]
    public void Display_DisplayConfigVideoSignalInfo_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>();
        Assert.Equal(48, size);
    }

    [Fact]
    public void Display_DisplayConfigTargetMode_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_TARGET_MODE>();
        Assert.Equal(48, size);
    }

    [Fact]
    public void Display_PointL_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.POINTL>();
        Assert.Equal(8, size);
    }

    [Fact]
    public void Display_DisplayConfigSourceMode_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_SOURCE_MODE>();
        Assert.Equal(20, size);
    }

    [Fact]
    public void Display_DisplayConfigModeInfoUnion_StructSize_MatchesWin32Standard()
    {
        int size = Marshal.SizeOf<Nitrous.Interop.User32.DISPLAYCONFIG_MODE_INFO_UNION>();
        Assert.Equal(48, size);
    }

    [Fact]
    public void Nvml_Utilization_StructSize_MatchesNvmlAbi()
    {
        int size = Marshal.SizeOf<Nitrous.Managers.NvidiaGpuManager.NvmlUtilization>();
        Assert.Equal(8, size);
    }

    [Fact]
    public void Nvml_Memory_StructSize_MatchesNvmlAbi()
    {
        int size = Marshal.SizeOf<Nitrous.Managers.NvidiaGpuManager.NvmlMemory>();
        Assert.Equal(24, size);
    }

    [Fact]
    public void Nvml_ProcessInfo_StructSize_MatchesNvmlAbi()
    {
        int size = Marshal.SizeOf<Nitrous.Managers.NvidiaGpuManager.NvmlProcessInfo>();
        Assert.Equal(24, size);
    }
}
