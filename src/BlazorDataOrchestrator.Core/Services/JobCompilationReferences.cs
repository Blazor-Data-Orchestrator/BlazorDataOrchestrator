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
}
