using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Resolves NuGet package dependencies for web-based compilation.
/// Caches resolved assemblies to minimize repeated downloads.
/// </summary>
public class WebNuGetResolverService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebNuGetResolverService> _logger;
    
    // Cache for resolved metadata references (keyed by "packageId:version")
    private static readonly ConcurrentDictionary<string, MetadataReference?> _referenceCache = new();
    
    // Cache for downloaded package bytes (keyed by "packageId:version")
    private static readonly ConcurrentDictionary<string, byte[]?> _packageCache = new();
    
    // NuGet API base URLs
    private const string NuGetServiceIndex = "https://api.nuget.org/v3/index.json";
    private const string NuGetPackageBaseUrl = "https://api.nuget.org/v3-flatcontainer";

    public WebNuGetResolverService(
        IHttpClientFactory httpClientFactory,
        ILogger<WebNuGetResolverService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves NuGet dependencies to MetadataReferences for compilation.
    /// </summary>
    /// <param name="dependencies">The list of NuGet dependencies from .nuspec.</param>
    /// <param name="targetFramework">The target framework (default: net9.0).</param>
    /// <returns>List of MetadataReferences for the resolved packages.</returns>
    public async Task<List<MetadataReference>> ResolveForCompilationAsync(
        List<NuGetDependencyInfo> dependencies,
        string targetFramework = "net9.0")
    {
        var references = new List<MetadataReference>();

        foreach (var dep in dependencies)
        {
            var cacheKey = $"{dep.PackageId.ToLowerInvariant()}:{dep.Version}";

            // Check cache first
            if (_referenceCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                references.Add(cached);
                _logger.LogDebug("Using cached reference for {PackageId} v{Version}", dep.PackageId, dep.Version);
                continue;
            }

            try
            {
                var packageReferences = await DownloadAndExtractReferencesAsync(dep, targetFramework);
                foreach (var reference in packageReferences)
                {
                    if (reference != null)
                    {
                        references.Add(reference);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve {PackageId} v{Version}",
                    dep.PackageId, dep.Version);
            }
        }

        return references;
    }

    /// <summary>
    /// Downloads a NuGet package and extracts DLLs as MetadataReferences.
    /// </summary>
    private async Task<List<MetadataReference>> DownloadAndExtractReferencesAsync(
        NuGetDependencyInfo dep,
        string targetFramework)
    {
        var references = new List<MetadataReference>();
        var packageId = dep.PackageId.ToLowerInvariant();
        var version = await ResolveVersionAsync(packageId, dep.Version);

        if (string.IsNullOrEmpty(version))
        {
            _logger.LogWarning("Could not resolve version for {PackageId}", dep.PackageId);
            return references;
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
                client.Timeout = TimeSpan.FromSeconds(30);
                
                _logger.LogInformation("Downloading NuGet package: {PackageId} v{Version}", dep.PackageId, version);
                packageBytes = await client.GetByteArrayAsync(packageUrl);
                _packageCache[cacheKey] = packageBytes;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to download package {PackageId} v{Version} from {Url}",
                    dep.PackageId, version, packageUrl);
                return references;
            }
        }

        // Extract DLLs from the package
        using var packageStream = new MemoryStream(packageBytes);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        // Find the best matching lib folder for our target framework
        var targetFrameworks = GetTargetFrameworkFolders(targetFramework);
        
        // First pass: collect all lib DLLs and their frameworks
        var libEntries = archive.Entries
            .Where(e => e.FullName.Replace('\\', '/').ToLowerInvariant().StartsWith("lib/") && 
                       e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!libEntries.Any())
        {
            _logger.LogWarning("No lib/*.dll entries found in package {PackageId}", dep.PackageId);
        }

        foreach (var entry in libEntries)
        {
            var entryPath = entry.FullName.Replace('\\', '/').ToLowerInvariant();
            
            // Extract the framework folder name from path like "lib/net6.0/Something.dll"
            var pathParts = entryPath.Split('/');
            if (pathParts.Length < 3) continue;
            
            var frameworkFolder = pathParts[1]; // The folder after "lib/"
            
            // Check if this framework is in our list of acceptable frameworks
            var matchedFramework = targetFrameworks.FirstOrDefault(tf => 
                frameworkFolder.Equals(tf, StringComparison.OrdinalIgnoreCase) ||
                frameworkFolder.StartsWith(tf, StringComparison.OrdinalIgnoreCase));

            if (matchedFramework == null)
            {
                // Log available frameworks for debugging
                _logger.LogDebug("Skipping {EntryPath} - framework {Framework} not in target list", 
                    entryPath, frameworkFolder);
                continue;
            }

            try
            {
                using var dllStream = entry.Open();
                using var ms = new MemoryStream();
                await dllStream.CopyToAsync(ms);
                var dllBytes = ms.ToArray();

                var reference = MetadataReference.CreateFromImage(dllBytes);
                references.Add(reference);
                
                // Cache the primary DLL reference
                var dllCacheKey = $"{cacheKey}:{entry.Name}";
                _referenceCache[dllCacheKey] = reference;
                
                _logger.LogInformation("Extracted reference: {DllName} from {PackageId} ({Framework})", 
                    entry.Name, dep.PackageId, frameworkFolder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract DLL {DllName} from {PackageId}",
                    entry.Name, dep.PackageId);
            }
        }

        // Also cache the first reference under the main cache key for quick lookup
        if (references.Any())
        {
            _referenceCache[cacheKey] = references.First();
        }

        return references;
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
    }
}
