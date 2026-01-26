using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Xml.Linq;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Resolves NuGet package dependencies for web-based compilation.
/// Caches resolved assemblies to minimize repeated downloads.
/// Supports transitive dependency resolution.
/// </summary>
public class WebNuGetResolverService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebNuGetResolverService> _logger;
    
    // Cache for resolved metadata references (keyed by "packageId:version")
    private static readonly ConcurrentDictionary<string, List<MetadataReference>> _referenceCache = new();
    
    // Cache for downloaded package bytes (keyed by "packageId:version")
    private static readonly ConcurrentDictionary<string, byte[]?> _packageCache = new();
    
    // Track packages being processed to prevent infinite loops
    private static readonly ConcurrentDictionary<string, bool> _processingPackages = new();
    
    // NuGet API base URLs
    private const string NuGetServiceIndex = "https://api.nuget.org/v3/index.json";
    private const string NuGetPackageBaseUrl = "https://api.nuget.org/v3-flatcontainer";
    
    // Maximum depth for transitive dependency resolution
    private const int MaxDependencyDepth = 5;

    public WebNuGetResolverService(
        IHttpClientFactory httpClientFactory,
        ILogger<WebNuGetResolverService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves NuGet dependencies to MetadataReferences for compilation.
    /// Includes transitive dependency resolution.
    /// </summary>
    /// <param name="dependencies">The list of NuGet dependencies from .nuspec.</param>
    /// <param name="targetFramework">The target framework (default: net10.0).</param>
    /// <returns>List of MetadataReferences for the resolved packages.</returns>
    public async Task<List<MetadataReference>> ResolveForCompilationAsync(
        List<NuGetDependencyInfo> dependencies,
        string targetFramework = "net10.0")
    {
        var references = new List<MetadataReference>();
        var processedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("Resolving {Count} NuGet dependencies for {TargetFramework} (with transitive resolution)", 
            dependencies.Count, targetFramework);

        // Resolve all dependencies including transitive ones
        await ResolvePackagesRecursivelyAsync(dependencies, targetFramework, references, processedPackages, 0);

        _logger.LogInformation("Total NuGet references resolved: {Count} (from {PackageCount} packages)", 
            references.Count, processedPackages.Count);
        
        return references;
    }

    /// <summary>
    /// Recursively resolves packages and their transitive dependencies.
    /// </summary>
    private async Task ResolvePackagesRecursivelyAsync(
        List<NuGetDependencyInfo> dependencies,
        string targetFramework,
        List<MetadataReference> references,
        HashSet<string> processedPackages,
        int depth)
    {
        if (depth > MaxDependencyDepth)
        {
            _logger.LogWarning("Maximum dependency depth ({MaxDepth}) reached, stopping recursion", MaxDependencyDepth);
            return;
        }

        foreach (var dep in dependencies)
        {
            var packageKey = $"{dep.PackageId.ToLowerInvariant()}:{dep.Version.ToLowerInvariant()}";

            // Skip if already processed
            if (processedPackages.Contains(packageKey))
            {
                continue;
            }

            // Check cache first
            if (_referenceCache.TryGetValue(packageKey, out var cached) && cached != null && cached.Any())
            {
                references.AddRange(cached);
                processedPackages.Add(packageKey);
                _logger.LogDebug("Using cached references for {PackageId} v{Version} ({RefCount} refs)", 
                    dep.PackageId, dep.Version, cached.Count);
                continue;
            }

            // Mark as processed to prevent loops
            if (!processedPackages.Add(packageKey))
            {
                continue;
            }

            try
            {
                _logger.LogInformation("Resolving package: {PackageId} v{Version} (depth: {Depth})", 
                    dep.PackageId, dep.Version, depth);
                
                var (packageReferences, transitiveDeps) = await DownloadAndExtractWithDependenciesAsync(dep, targetFramework);
                
                if (packageReferences.Any())
                {
                    references.AddRange(packageReferences);
                    _referenceCache[packageKey] = packageReferences;
                    _logger.LogInformation("Extracted {Count} references from {PackageId}", 
                        packageReferences.Count, dep.PackageId);
                }

                // Recursively resolve transitive dependencies
                if (transitiveDeps.Any())
                {
                    _logger.LogDebug("Package {PackageId} has {Count} transitive dependencies", 
                        dep.PackageId, transitiveDeps.Count);
                    await ResolvePackagesRecursivelyAsync(transitiveDeps, targetFramework, references, processedPackages, depth + 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve {PackageId} v{Version}",
                    dep.PackageId, dep.Version);
            }
        }
    }

    /// <summary>
    /// Downloads a NuGet package and extracts DLLs as MetadataReferences.
    /// Also extracts transitive dependencies from the package's .nuspec.
    /// </summary>
    private async Task<(List<MetadataReference> References, List<NuGetDependencyInfo> Dependencies)> 
        DownloadAndExtractWithDependenciesAsync(
            NuGetDependencyInfo dep,
            string targetFramework)
    {
        var references = new List<MetadataReference>();
        var transitiveDeps = new List<NuGetDependencyInfo>();
        var packageId = dep.PackageId.ToLowerInvariant();
        var version = await ResolveVersionAsync(packageId, dep.Version);

        if (string.IsNullOrEmpty(version))
        {
            _logger.LogWarning("Could not resolve version for {PackageId}", dep.PackageId);
            return (references, transitiveDeps);
        }

        var cacheKey = $"{packageId}:{version}";

        // Try to get from package cache
        byte[]? packageBytes;
        if (!_packageCache.TryGetValue(cacheKey, out packageBytes) || packageBytes == null)
        {
            // Download the package
            var packageUrl = $"{NuGetPackageBaseUrl}/{packageId}/{version}/{packageId}.{version}.nupkg";
            
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(60);
                
                _logger.LogInformation("Downloading NuGet package: {PackageId} v{Version}", dep.PackageId, version);
                packageBytes = await client.GetByteArrayAsync(packageUrl);
                _packageCache[cacheKey] = packageBytes;
                _logger.LogDebug("Downloaded {Size} bytes for {PackageId}", packageBytes.Length, dep.PackageId);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to download package {PackageId} v{Version} from {Url}",
                    dep.PackageId, version, packageUrl);
                return (references, transitiveDeps);
            }
        }

        // Extract DLLs and .nuspec from the package
        using var packageStream = new MemoryStream(packageBytes);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        // First, extract .nuspec to get transitive dependencies
        var nuspecEntry = archive.Entries.FirstOrDefault(e => 
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        
        if (nuspecEntry != null)
        {
            try
            {
                using var nuspecStream = nuspecEntry.Open();
                using var reader = new StreamReader(nuspecStream);
                var nuspecContent = await reader.ReadToEndAsync();
                transitiveDeps = ParseNuspecDependencies(nuspecContent, targetFramework);
                _logger.LogDebug("Parsed {Count} transitive dependencies from {PackageId} .nuspec", 
                    transitiveDeps.Count, dep.PackageId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse .nuspec from {PackageId}", dep.PackageId);
            }
        }

        // Find the best matching lib folder for our target framework
        var targetFrameworks = GetTargetFrameworkFolders(targetFramework);
        
        // First pass: collect all lib DLLs and group by framework
        var libEntries = archive.Entries
            .Where(e => e.FullName.Replace('\\', '/').ToLowerInvariant().StartsWith("lib/") && 
                       e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("Package {PackageId} has {Count} lib DLL entries: {Entries}", 
            dep.PackageId, libEntries.Count,
            string.Join(", ", libEntries.Select(e => e.FullName)));

        if (!libEntries.Any())
        {
            _logger.LogWarning("No lib/*.dll entries found in package {PackageId}. All entries: {Entries}", 
                dep.PackageId, 
                string.Join(", ", archive.Entries.Take(20).Select(e => e.FullName)));
            return (references, transitiveDeps);
        }

        // Group entries by framework folder
        var entriesByFramework = libEntries
            .Select(entry => {
                var entryPath = entry.FullName.Replace('\\', '/');
                var pathParts = entryPath.Split('/');
                var framework = pathParts.Length >= 2 ? pathParts[1].ToLowerInvariant() : "";
                return (Entry: entry, Framework: framework);
            })
            .GroupBy(x => x.Framework)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Entry).ToList());

        _logger.LogDebug("Package {PackageId} has frameworks: {Frameworks}", 
            dep.PackageId, string.Join(", ", entriesByFramework.Keys));

        // Find the best matching framework in priority order
        string? bestFramework = null;
        foreach (var tf in targetFrameworks)
        {
            if (entriesByFramework.ContainsKey(tf))
            {
                bestFramework = tf;
                break;
            }
        }

        if (bestFramework == null)
        {
            _logger.LogWarning("No compatible framework found for {PackageId}. Available: {Available}, Wanted: {Wanted}",
                dep.PackageId, 
                string.Join(", ", entriesByFramework.Keys),
                string.Join(", ", targetFrameworks.Take(5)));
            return (references, transitiveDeps);
        }

        _logger.LogInformation("Selected framework {Framework} for {PackageId}", bestFramework, dep.PackageId);

        // Extract DLLs from the best matching framework
        foreach (var entry in entriesByFramework[bestFramework])
        {
            try
            {
                using var dllStream = entry.Open();
                using var ms = new MemoryStream();
                await dllStream.CopyToAsync(ms);
                var dllBytes = ms.ToArray();

                var reference = MetadataReference.CreateFromImage(dllBytes);
                references.Add(reference);
                
                _logger.LogInformation("Extracted reference: {DllName} from {PackageId} ({Framework})", 
                    entry.Name, dep.PackageId, bestFramework);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract DLL {DllName} from {PackageId}",
                    entry.Name, dep.PackageId);
            }
        }

        return (references, transitiveDeps);
    }

    /// <summary>
    /// Parses dependencies from a .nuspec file content.
    /// </summary>
    private List<NuGetDependencyInfo> ParseNuspecDependencies(string nuspecContent, string targetFramework)
    {
        var dependencies = new List<NuGetDependencyInfo>();
        
        try
        {
            var doc = XDocument.Parse(nuspecContent);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            
            // Find the dependency group that matches our target framework
            var dependencyGroups = doc.Descendants(ns + "group").ToList();
            
            _logger.LogDebug("Found {Count} dependency groups in nuspec", dependencyGroups.Count);
            
            // Try to find a matching group - use a flexible matching approach
            XElement? matchingGroup = null;
            
            // Define framework priorities for matching (higher priority first)
            // Match based on the actual .nuspec format (e.g., ".NETStandard2.0")
            var frameworkPriorities = new[]
            {
                ".NETStandard2.1", "netstandard2.1",
                ".NETStandard2.0", "netstandard2.0", 
                ".NETStandard1.6", "netstandard1.6",
                ".NETStandard1.3", "netstandard1.3",
                "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
                ".NETCoreApp3.1", "netcoreapp3.1"
            };
            
            foreach (var priority in frameworkPriorities)
            {
                matchingGroup = dependencyGroups.FirstOrDefault(g =>
                {
                    var tfAttr = g.Attribute("targetFramework")?.Value;
                    if (tfAttr == null) return false;
                    return tfAttr.Equals(priority, StringComparison.OrdinalIgnoreCase);
                });
                
                if (matchingGroup != null)
                {
                    _logger.LogDebug("Matched dependency group with targetFramework: {TF}", 
                        matchingGroup.Attribute("targetFramework")?.Value);
                    break;
                }
            }
            
            // If no matching group, use the first group (often the most general)
            IEnumerable<XElement> dependencyElements;
            if (matchingGroup != null)
            {
                dependencyElements = matchingGroup.Elements(ns + "dependency");
            }
            else if (dependencyGroups.Any())
            {
                // Use the last group (often netstandard which is more compatible)
                matchingGroup = dependencyGroups.Last();
                dependencyElements = matchingGroup.Elements(ns + "dependency");
                _logger.LogDebug("No exact match, using last group with targetFramework: {TF}",
                    matchingGroup.Attribute("targetFramework")?.Value);
            }
            else
            {
                // No groups - get all dependencies directly
                dependencyElements = doc.Descendants(ns + "dependency");
            }
            
            foreach (var dep in dependencyElements)
            {
                var packageId = dep.Attribute("id")?.Value;
                var version = dep.Attribute("version")?.Value ?? "";
                
                if (!string.IsNullOrEmpty(packageId))
                {
                    // Skip common system packages that are included with .NET
                    if (IsSystemPackage(packageId))
                    {
                        _logger.LogDebug("Skipping system package: {PackageId}", packageId);
                        continue;
                    }
                    
                    // Clean up version ranges like "[1.3.3, 2.0.0)" to just "1.3.3"
                    version = CleanVersionSpec(version);
                    
                    dependencies.Add(new NuGetDependencyInfo
                    {
                        PackageId = packageId,
                        Version = version,
                        TargetFramework = targetFramework
                    });
                    
                    _logger.LogDebug("Added dependency: {PackageId} v{Version}", packageId, version);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse .nuspec dependencies");
        }
        
        _logger.LogInformation("Parsed {Count} dependencies from nuspec", dependencies.Count);
        return dependencies;
    }

    /// <summary>
    /// Cleans up a NuGet version specification to extract a usable version.
    /// Handles ranges like "[1.3.3, 2.0.0)" by extracting the minimum version.
    /// </summary>
    private static string CleanVersionSpec(string versionSpec)
    {
        if (string.IsNullOrEmpty(versionSpec))
            return versionSpec;
            
        // Remove brackets and parentheses
        var cleaned = versionSpec.Trim('[', ']', '(', ')');
        
        // If there's a comma (range), take the first part (minimum version)
        if (cleaned.Contains(','))
        {
            cleaned = cleaned.Split(',')[0].Trim();
        }
        
        return cleaned;
    }

    /// <summary>
    /// Checks if a package is a system package that should be skipped.
    /// These packages are typically already available in the .NET runtime.
    /// </summary>
    private static bool IsSystemPackage(string packageId)
    {
        // Only skip packages that are guaranteed to be in the runtime
        // Be conservative - it's better to download a duplicate than miss a dependency
        var systemPrefixes = new[]
        {
            "Microsoft.NETCore.App",
            "Microsoft.NETCore.Platforms",
            "NETStandard.Library",
            "runtime."
        };
        
        // Exact match packages
        var exactMatches = new[]
        {
            "Microsoft.CSharp",
            "System.Runtime",
            "System.Runtime.Loader"
        };
        
        return systemPrefixes.Any(prefix => 
            packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            exactMatches.Any(exact => 
            packageId.Equals(exact, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a version specifier to an actual version number.
    /// </summary>
    private async Task<string?> ResolveVersionAsync(string packageId, string versionSpec)
    {
        // If we have a concrete version (no range specifiers), use it directly
        if (!string.IsNullOrEmpty(versionSpec) && 
            !versionSpec.Contains('[') && 
            !versionSpec.Contains('(') &&
            !versionSpec.Contains(',') &&
            !versionSpec.Contains('*'))
        {
            return versionSpec.ToLowerInvariant();
        }

        // Otherwise, fetch the latest version from NuGet
        try
        {
            var client = _httpClientFactory.CreateClient();
            var versionsUrl = $"{NuGetPackageBaseUrl}/{packageId}/index.json";
            
            var response = await client.GetStringAsync(versionsUrl);
            var doc = System.Text.Json.JsonDocument.Parse(response);
            
            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                var versionList = versions.EnumerateArray()
                    .Select(v => v.GetString())
                    .Where(v => v != null && !v.Contains("-")) // Exclude prerelease
                    .ToList();

                if (versionList.Any())
                {
                    // Return the latest stable version
                    return versionList.Last();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve version for {PackageId}", packageId);
        }

        return null;
    }

    /// <summary>
    /// Gets the folder names to look for based on target framework, in order of preference.
    /// </summary>
    private static List<string> GetTargetFrameworkFolders(string targetFramework)
    {
        var folders = new List<string>();
        
        // Try exact match first, then fallback to compatible frameworks
        // Include common variations and .NET Standard as fallbacks
        switch (targetFramework.ToLowerInvariant())
        {
            case "net10.0":
                folders.AddRange(new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" });
                break;
            case "net9.0":
                folders.AddRange(new[] { "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" });
                break;
            case "net8.0":
                folders.AddRange(new[] { "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" });
                break;
            case "net7.0":
                folders.AddRange(new[] { "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" });
                break;
            case "net6.0":
                folders.AddRange(new[] { "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" });
                break;
            default:
                folders.AddRange(new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" });
                break;
        }

        return folders;
    }

    /// <summary>
    /// Clears the reference cache. Useful for testing or when packages need to be re-downloaded.
    /// </summary>
    public static void ClearCache()
    {
        _referenceCache.Clear();
        _packageCache.Clear();
        _processingPackages.Clear();
    }
}
