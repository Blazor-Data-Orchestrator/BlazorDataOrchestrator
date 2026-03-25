using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for resolving NuGet package dependencies and downloading their assemblies.
/// Uses the NuGet v3 HTTP API directly (no .NET SDK required).
/// Downloads .nupkg files, extracts DLLs, and caches them locally.
/// Supports transitive dependency resolution.
/// </summary>
public class NuGetResolverService
{
    private const string NuGetPackageBaseUrl = "https://api.nuget.org/v3-flatcontainer";
    private const int MaxDependencyDepth = 5;

    private readonly string _cacheBasePath;
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    // In-memory cache of extracted assembly paths per package key
    private static readonly ConcurrentDictionary<string, List<string>> _assemblyCache = new();

    public NuGetResolverService()
    {
        // Cache extracted DLLs under user profile or temp
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
        {
            userProfile = Path.GetTempPath();
        }
        _cacheBasePath = Path.Combine(userProfile, ".blazor-orchestrator", "nuget-cache");
        Directory.CreateDirectory(_cacheBasePath);
    }

    /// <summary>
    /// Resolves NuGet dependencies and returns paths to all required assemblies.
    /// Uses the NuGet HTTP API directly — does NOT require the .NET SDK.
    /// </summary>
    /// <param name="dependencies">The dependencies to resolve</param>
    /// <param name="targetFramework">Target framework (e.g., "net10.0")</param>
    /// <param name="logs">Log output list</param>
    /// <returns>Resolution result with assembly paths</returns>
    public async Task<NuGetResolutionResult> ResolveAsync(
        List<NuGetDependency> dependencies,
        string targetFramework,
        List<string> logs)
    {
        var result = new NuGetResolutionResult();

        if (dependencies == null || dependencies.Count == 0)
        {
            result.Success = true;
            logs.Add("No NuGet dependencies to resolve.");
            return result;
        }

        logs.Add($"Resolving {dependencies.Count} NuGet dependencies for {targetFramework}...");

        try
        {
            var processedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allAssemblyPaths = new List<string>();

            await ResolvePackagesRecursivelyAsync(
                dependencies, targetFramework, allAssemblyPaths, processedPackages, logs, 0);

            result.AssemblyPaths = allAssemblyPaths;
            result.Success = true;

            logs.Add($"Resolved {allAssemblyPaths.Count} assemblies from {processedPackages.Count} NuGet packages.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to resolve NuGet dependencies: {ex.Message}";
            logs.Add($"Error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Recursively resolves packages and their transitive dependencies.
    /// </summary>
    private async Task ResolvePackagesRecursivelyAsync(
        List<NuGetDependency> dependencies,
        string targetFramework,
        List<string> assemblyPaths,
        HashSet<string> processedPackages,
        List<string> logs,
        int depth)
    {
        if (depth > MaxDependencyDepth)
        {
            logs.Add($"Maximum dependency depth ({MaxDependencyDepth}) reached, stopping recursion.");
            return;
        }

        foreach (var dep in dependencies)
        {
            var packageKey = $"{dep.PackageId.ToLowerInvariant()}:{dep.Version.ToLowerInvariant()}";

            // Skip if already processed
            if (!processedPackages.Add(packageKey))
                continue;

            // Skip system packages
            if (IsSystemPackage(dep.PackageId))
            {
                logs.Add($"  Skipping system package: {dep.PackageId}");
                continue;
            }

            // Check in-memory cache
            if (_assemblyCache.TryGetValue(packageKey, out var cachedPaths) && cachedPaths.Any())
            {
                assemblyPaths.AddRange(cachedPaths);
                logs.Add($"  Using cached assemblies for {dep.PackageId} v{dep.Version} ({cachedPaths.Count} DLLs)");
                continue;
            }

            try
            {
                logs.Add($"  Resolving: {dep.PackageId} v{dep.Version} (depth: {depth})");

                var (dlls, transitiveDeps) = await DownloadAndExtractAsync(
                    dep.PackageId, dep.Version, targetFramework, logs);

                if (dlls.Any())
                {
                    assemblyPaths.AddRange(dlls);
                    _assemblyCache[packageKey] = dlls;
                    logs.Add($"  + Extracted {dlls.Count} DLLs from {dep.PackageId}");
                }

                // Recursively resolve transitive dependencies
                if (transitiveDeps.Any())
                {
                    logs.Add($"  {dep.PackageId} has {transitiveDeps.Count} transitive dependencies");
                    await ResolvePackagesRecursivelyAsync(
                        transitiveDeps, targetFramework, assemblyPaths, processedPackages, logs, depth + 1);
                }
            }
            catch (Exception ex)
            {
                logs.Add($"  Warning: Failed to resolve {dep.PackageId} v{dep.Version}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Downloads a NuGet package from nuget.org and extracts the best-matching DLLs.
    /// Also parses the embedded .nuspec for transitive dependencies.
    /// </summary>
    private async Task<(List<string> AssemblyPaths, List<NuGetDependency> TransitiveDeps)> DownloadAndExtractAsync(
        string packageId,
        string versionSpec,
        string targetFramework,
        List<string> logs)
    {
        var assemblyPaths = new List<string>();
        var transitiveDeps = new List<NuGetDependency>();

        var packageIdLower = packageId.ToLowerInvariant();
        var version = await ResolveVersionAsync(packageIdLower, versionSpec);
        if (string.IsNullOrEmpty(version))
        {
            logs.Add($"  Could not resolve version for {packageId}");
            return (assemblyPaths, transitiveDeps);
        }

        // Check if already extracted on disk
        var packageCacheDir = Path.Combine(_cacheBasePath, packageIdLower, version);
        var libDir = Path.Combine(packageCacheDir, "lib");
        var nuspecMarker = Path.Combine(packageCacheDir, ".extracted");

        if (Directory.Exists(libDir) && File.Exists(nuspecMarker))
        {
            // Already extracted — read from disk cache
            assemblyPaths = FindBestFrameworkDlls(libDir, targetFramework);
            transitiveDeps = await ReadCachedTransitiveDepsAsync(packageCacheDir, targetFramework);
            return (assemblyPaths, transitiveDeps);
        }

        // Download the .nupkg
        var packageUrl = $"{NuGetPackageBaseUrl}/{packageIdLower}/{version}/{packageIdLower}.{version}.nupkg";

        byte[] packageBytes;
        try
        {
            packageBytes = await _httpClient.GetByteArrayAsync(packageUrl);
            logs.Add($"  Downloaded {packageId} v{version} ({packageBytes.Length / 1024}KB)");
        }
        catch (HttpRequestException ex)
        {
            logs.Add($"  Failed to download {packageId} v{version}: {ex.Message}");
            return (assemblyPaths, transitiveDeps);
        }

        // Extract the .nupkg (it's a ZIP file)
        Directory.CreateDirectory(packageCacheDir);

        using var packageStream = new MemoryStream(packageBytes);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        // 1. Extract .nuspec for transitive dependencies
        var nuspecEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        if (nuspecEntry != null)
        {
            try
            {
                using var nuspecStream = nuspecEntry.Open();
                using var reader = new StreamReader(nuspecStream);
                var nuspecContent = await reader.ReadToEndAsync();

                // Cache the nuspec on disk
                var nuspecCachePath = Path.Combine(packageCacheDir, "cached.nuspec");
                await File.WriteAllTextAsync(nuspecCachePath, nuspecContent);

                transitiveDeps = ParseNuspecDependencies(nuspecContent, targetFramework);
            }
            catch
            {
                // Ignore .nuspec parse errors
            }
        }

        // 2. Extract lib/**/*.dll entries to disk
        var libEntries = archive.Entries
            .Where(e => e.FullName.Replace('\\', '/').StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
                       e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in libEntries)
        {
            var relativePath = entry.FullName.Replace('\\', '/');
            var destPath = Path.Combine(packageCacheDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null)
                Directory.CreateDirectory(destDir);

            using var entryStream = entry.Open();
            using var fileStream = File.Create(destPath);
            await entryStream.CopyToAsync(fileStream);
        }

        // Write extraction marker
        await File.WriteAllTextAsync(nuspecMarker, DateTime.UtcNow.ToString("O"));

        // Find best-matching DLLs
        if (Directory.Exists(libDir))
        {
            assemblyPaths = FindBestFrameworkDlls(libDir, targetFramework);
        }

        return (assemblyPaths, transitiveDeps);
    }

    /// <summary>
    /// Finds the best framework-matched DLLs from an extracted lib/ directory.
    /// </summary>
    private List<string> FindBestFrameworkDlls(string libDir, string targetFramework)
    {
        var result = new List<string>();
        var targetFrameworks = GetTargetFrameworkFolders(targetFramework);

        // Get framework subdirectories
        var frameworkDirs = Directory.GetDirectories(libDir)
            .Select(d => new DirectoryInfo(d))
            .ToDictionary(d => d.Name.ToLowerInvariant(), d => d.FullName);

        // Find best match
        string? bestDir = null;
        foreach (var tf in targetFrameworks)
        {
            if (frameworkDirs.TryGetValue(tf, out var dir))
            {
                bestDir = dir;
                break;
            }
        }

        if (bestDir == null)
            return result;

        // Collect all DLLs in the matched framework folder (non-recursive)
        foreach (var dll in Directory.GetFiles(bestDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            // Skip resource/satellite assemblies
            var fileName = Path.GetFileName(dll);
            if (!fileName.Contains(".resources.", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(dll);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads cached transitive dependencies from a previously extracted package.
    /// </summary>
    private async Task<List<NuGetDependency>> ReadCachedTransitiveDepsAsync(
        string packageCacheDir, string targetFramework)
    {
        var nuspecPath = Path.Combine(packageCacheDir, "cached.nuspec");
        if (!File.Exists(nuspecPath))
            return new List<NuGetDependency>();

        var content = await File.ReadAllTextAsync(nuspecPath);
        return ParseNuspecDependencies(content, targetFramework);
    }

    /// <summary>
    /// Parses dependencies from a .nuspec XML file.
    /// </summary>
    private List<NuGetDependency> ParseNuspecDependencies(string nuspecContent, string targetFramework)
    {
        var dependencies = new List<NuGetDependency>();

        try
        {
            var doc = XDocument.Parse(nuspecContent);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var dependencyGroups = doc.Descendants(ns + "group").ToList();

            // Framework matching priorities
            var frameworkPriorities = new[]
            {
                ".NETStandard2.1", "netstandard2.1",
                ".NETStandard2.0", "netstandard2.0",
                ".NETStandard1.6", "netstandard1.6",
                ".NETStandard1.3", "netstandard1.3",
                "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
                ".NETCoreApp3.1", "netcoreapp3.1"
            };

            XElement? matchingGroup = null;
            foreach (var priority in frameworkPriorities)
            {
                matchingGroup = dependencyGroups.FirstOrDefault(g =>
                {
                    var tfAttr = g.Attribute("targetFramework")?.Value;
                    return tfAttr != null && tfAttr.Equals(priority, StringComparison.OrdinalIgnoreCase);
                });
                if (matchingGroup != null) break;
            }

            IEnumerable<XElement> depElements;
            if (matchingGroup != null)
            {
                depElements = matchingGroup.Elements(ns + "dependency");
            }
            else if (dependencyGroups.Any())
            {
                depElements = dependencyGroups.Last().Elements(ns + "dependency");
            }
            else
            {
                depElements = doc.Descendants(ns + "dependency");
            }

            foreach (var dep in depElements)
            {
                var pkgId = dep.Attribute("id")?.Value;
                var version = dep.Attribute("version")?.Value ?? "";

                if (!string.IsNullOrEmpty(pkgId) && !IsSystemPackage(pkgId))
                {
                    version = CleanVersionSpec(version);
                    dependencies.Add(new NuGetDependency
                    {
                        PackageId = pkgId,
                        Version = version
                    });
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return dependencies;
    }

    /// <summary>
    /// Resolves a version spec to a concrete version. If it's already concrete, returns it directly.
    /// Otherwise fetches available versions from NuGet and picks the latest stable.
    /// </summary>
    private async Task<string?> ResolveVersionAsync(string packageIdLower, string versionSpec)
    {
        // Clean version spec first
        var cleaned = CleanVersionSpec(versionSpec).ToLowerInvariant();

        // If it looks like a concrete version, use it directly
        if (!string.IsNullOrEmpty(cleaned) &&
            !cleaned.Contains('*') &&
            !cleaned.Contains('[') &&
            !cleaned.Contains('('))
        {
            return cleaned;
        }

        // Otherwise, fetch latest stable from NuGet
        try
        {
            var versionsUrl = $"{NuGetPackageBaseUrl}/{packageIdLower}/index.json";
            var response = await _httpClient.GetStringAsync(versionsUrl);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                var versionList = versions.EnumerateArray()
                    .Select(v => v.GetString())
                    .Where(v => v != null && !v!.Contains('-'))  // Exclude prerelease
                    .ToList();

                if (versionList.Any())
                    return versionList.Last();
            }
        }
        catch
        {
            // Fall back to cleaned version
        }

        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Cleans up a NuGet version range to extract the minimum usable version.
    /// </summary>
    private static string CleanVersionSpec(string versionSpec)
    {
        if (string.IsNullOrEmpty(versionSpec))
            return versionSpec;

        var cleaned = versionSpec.Trim('[', ']', '(', ')');
        if (cleaned.Contains(','))
            cleaned = cleaned.Split(',')[0].Trim();

        return cleaned;
    }

    /// <summary>
    /// Checks if a package is a .NET system package that should be skipped.
    /// </summary>
    private static bool IsSystemPackage(string packageId)
    {
        var systemPrefixes = new[]
        {
            "Microsoft.NETCore.App",
            "Microsoft.NETCore.Platforms",
            "NETStandard.Library",
            "runtime."
        };

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
    /// Gets target framework folder names in priority order for matching.
    /// </summary>
    private static List<string> GetTargetFrameworkFolders(string targetFramework)
    {
        return targetFramework.ToLowerInvariant() switch
        {
            "net10.0" => new List<string> { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" },
            "net9.0" => new List<string> { "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" },
            "net8.0" => new List<string> { "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" },
            _ => new List<string> { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3" }
        };
    }

    /// <summary>
    /// Clears the in-memory assembly cache.
    /// </summary>
    public static void ClearCache()
    {
        _assemblyCache.Clear();
    }
}
