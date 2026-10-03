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
}
