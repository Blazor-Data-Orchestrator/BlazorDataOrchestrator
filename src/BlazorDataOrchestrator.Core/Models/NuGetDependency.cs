namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Represents a NuGet package dependency for package creation.
/// Used when building NuGet packages from job code.
/// </summary>
public class PackageDependency
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";

    public override string ToString() => $"{Id} {Version}";
}

/// <summary>
/// Represents the dependencies configuration file structure.
/// </summary>
public class DependenciesConfig
{
    public List<PackageDependency> Dependencies { get; set; } = new();
}

/// <summary>
/// Represents a NuGet package dependency extracted from a .nuspec file.
/// </summary>
public class NuGetDependency
{
    /// <summary>
    /// The NuGet package ID (e.g., "SendGrid", "Azure.Storage.Blobs").
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// The version or version range (e.g., "9.29.3", "[12.0.0, 13.0.0)").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Optional exclude attribute from the dependency.
    /// </summary>
    public string? Exclude { get; set; }

    public override string ToString() => $"{PackageId} {Version}";
}

/// <summary>
/// Represents a group of dependencies for a specific target framework.
/// </summary>
public class NuGetDependencyGroup
{
    /// <summary>
    /// The target framework moniker (e.g., "net10.0", "net8.0", "netstandard2.0").
    /// Null for framework-agnostic dependencies.
    /// </summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// The list of dependencies in this group.
    /// </summary>
    public List<NuGetDependency> Dependencies { get; set; } = new();

    public override string ToString() => $"[{TargetFramework ?? "any"}] {Dependencies.Count} dependencies";
}

/// <summary>
/// Result of NuGet package dependency resolution.
/// </summary>
public class NuGetResolutionResult
{
    /// <summary>
    /// Whether the resolution was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// List of resolved assembly paths that should be referenced.
    /// </summary>
    public List<string> AssemblyPaths { get; set; } = new();

    /// <summary>
    /// Error message if resolution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Log messages from the resolution process.
    /// </summary>
    public List<string> Logs { get; set; } = new();
}
