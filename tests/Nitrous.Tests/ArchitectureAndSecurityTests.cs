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
}
