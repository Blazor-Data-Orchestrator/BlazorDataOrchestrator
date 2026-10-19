using System.Reflection;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Assemblies every job compilation references, shared by the Agent and the designer so the two cannot drift.
/// </summary>
public static class JobCompilationReferences
{
    public static IReadOnlyList<Assembly> Common { get; } = new[]
    {
        typeof(System.Text.Json.JsonSerializer).Assembly,
        typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly,
        typeof(JobManager).Assembly,
        // Required by the compiler for any use of 'dynamic' (CS0656 otherwise).
        typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly
    };

    private static readonly Lazy<IReadOnlyList<string>> FrameworkPaths = new(() =>
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(runtimeDirectory))
            return Array.Empty<string>();

        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

        return tpa
            .Where(p => string.Equals(Path.GetDirectoryName(p), runtimeDirectory, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(p))
            .ToArray();
    });

    /// <summary>
    /// Every assembly of the .NET runtime the host runs on (e.g. System.Data.Common, System.Collections.NonGeneric,
    /// System.Private.Xml). The Agent compiles against the full Trusted Platform Assemblies list, so the designer
    /// references these too; otherwise code the Agent accepts is rejected by Save &amp; Compile.
    /// </summary>
    public static IReadOnlyList<string> FrameworkAssemblyPaths => FrameworkPaths.Value;
}
