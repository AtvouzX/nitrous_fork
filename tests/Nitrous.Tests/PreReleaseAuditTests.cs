using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class PreReleaseAuditTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "app", "Nitrous.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root containing app/Nitrous.csproj");
    }

    [Fact]
    public void VersionDeclarations_CsprojAndAssembly_MatchUpdateManagerCurrentVersion()
    {
        string repoRoot = FindRepoRoot();
        string csprojPath = Path.Combine(repoRoot, "app", "Nitrous.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found at: {csprojPath}");

        var doc = XDocument.Load(csprojPath);
        string? csprojVersion = doc.Descendants("Version").FirstOrDefault()?.Value?.Trim();
        string? csprojAssemblyVersion = doc.Descendants("AssemblyVersion").FirstOrDefault()?.Value?.Trim();
        string? csprojFileVersion = doc.Descendants("FileVersion").FirstOrDefault()?.Value?.Trim();

        string currentVersion = UpdateManager.CurrentVersion;

        // 1. Csproj Version matches UpdateManager.CurrentVersion
        Assert.NotNull(csprojVersion);
        Assert.Equal(currentVersion, csprojVersion);

        // 2. AssemblyVersion and FileVersion enforce standard 4-part notation with trailing .0
        Assert.NotNull(csprojAssemblyVersion);
        Assert.Equal($"{currentVersion}.0", csprojAssemblyVersion);

        Assert.NotNull(csprojFileVersion);
        Assert.Equal($"{currentVersion}.0", csprojFileVersion);

        // 3. CurrentVersion is valid 3-part SemVer
        var parts = currentVersion.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.True(int.TryParse(parts[0], out int major) && major >= 0, "Major version must be a non-negative integer.");
        Assert.True(int.TryParse(parts[1], out int minor) && minor >= 0, "Minor version must be a non-negative integer.");
        Assert.True(int.TryParse(parts[2], out int patch) && patch >= 0, "Patch version must be a non-negative integer.");
    }

    [Fact]
    public void ProjectConfiguration_EnforcesOptimizedReleaseSettings()
    {
        string repoRoot = FindRepoRoot();
        string csprojPath = Path.Combine(repoRoot, "app", "Nitrous.csproj");
        var doc = XDocument.Load(csprojPath);

        string? targetFramework = doc.Descendants("TargetFramework").FirstOrDefault()?.Value?.Trim();
        string? rootNamespace = doc.Descendants("RootNamespace").FirstOrDefault()?.Value?.Trim();
        string? nullable = doc.Descendants("Nullable").FirstOrDefault()?.Value?.Trim();
        string? useWpf = doc.Descendants("UseWPF").FirstOrDefault()?.Value?.Trim();
        string? useWindowsForms = doc.Descendants("UseWindowsForms").FirstOrDefault()?.Value?.Trim();

        Assert.Equal("net10.0-windows", targetFramework);
        Assert.Equal("Nitrous", rootNamespace);
        Assert.Equal("enable", nullable);
        Assert.Equal("true", useWpf);
        Assert.Equal("true", useWindowsForms);
    }

    [Fact]
    public void AssemblyAttributes_MatchCurrentVersion()
    {
        var assembly = typeof(UpdateManager).Assembly;

        var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        Assert.NotNull(fileVersionAttr);
        Assert.StartsWith(UpdateManager.CurrentVersion, fileVersionAttr.Version);

        var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (infoVersionAttr != null)
        {
            Assert.StartsWith(UpdateManager.CurrentVersion, infoVersionAttr.InformationalVersion);
        }
    }

    [Fact]
    public void PublishedBinary_IfPresent_HasValidMetadataAndLightweightSize()
    {
        string repoRoot = FindRepoRoot();
        string publishExe = Path.Combine(repoRoot, "publish", "Nitrous.exe");

        if (File.Exists(publishExe))
        {
            var vi = FileVersionInfo.GetVersionInfo(publishExe);
            Assert.StartsWith(UpdateManager.CurrentVersion, vi.FileVersion ?? "");

            var fi = new FileInfo(publishExe);
            // Standalone single-file binary should remain lightweight (typically 1.0 - 2.5 MB)
            double sizeMb = fi.Length / (1024.0 * 1024.0);
            Assert.True(sizeMb is > 0.5 and < 10.0, $"Published binary size ({sizeMb:F2} MB) is unexpected.");
        }
    }
}
