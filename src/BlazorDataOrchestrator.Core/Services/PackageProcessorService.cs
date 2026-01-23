using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for handling NuGet package download, extraction, and validation.
/// Used by the Agent to process job packages.
/// </summary>
public class PackageProcessorService
{
    private readonly JobStorageService _storageService;

    public PackageProcessorService(JobStorageService storageService)
    {
        _storageService = storageService;
    }

    /// <summary>
    /// Downloads a package from blob storage and extracts it to a local directory.
    /// </summary>
    /// <param name="blobName">The blob name from Job.JobCodeFile</param>
    /// <param name="extractPath">The directory to extract the package to</param>
    /// <returns>True if successful, false if package doesn't exist</returns>
    public async Task<bool> DownloadAndExtractPackageAsync(string blobName, string extractPath)
    {
        if (string.IsNullOrEmpty(blobName))
            return false;

        // Ensure extract path exists and is clean
        if (Directory.Exists(extractPath))
        {
            Directory.Delete(extractPath, true);
        }
        Directory.CreateDirectory(extractPath);

        // Download to temp file
        var tempPackagePath = Path.Combine(extractPath, "_package.nupkg");
        var downloaded = await _storageService.DownloadPackageAsync(blobName, tempPackagePath);

        if (!downloaded)
            return false;

        // Extract the NuGet package (it's a ZIP file)
        ZipFile.ExtractToDirectory(tempPackagePath, extractPath, overwriteFiles: true);

        // Clean up the temp package file
        File.Delete(tempPackagePath);

        return true;
    }

    /// <summary>
    /// Validates the NuSpec structure of an extracted package.
    /// </summary>
    /// <param name="extractedPath">The path where the package was extracted</param>
    /// <returns>Validation result with success status and any error messages</returns>
    public async Task<PackageValidationResult> ValidateNuSpecAsync(string extractedPath)
    {
        var result = new PackageValidationResult();

        // Check for .nuspec file
        var nuspecFiles = Directory.GetFiles(extractedPath, "*.nuspec", SearchOption.TopDirectoryOnly);
        if (nuspecFiles.Length == 0)
        {
            result.Errors.Add("No .nuspec file found in package root.");
            return result;
        }

        // Parse the .nuspec file
        try
        {
            var nuspecPath = nuspecFiles[0];
            var nuspecXml = await File.ReadAllTextAsync(nuspecPath);
            var doc = XDocument.Parse(nuspecXml);

            // Extract package metadata
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var metadata = doc.Root?.Element(ns + "metadata");

            if (metadata != null)
            {
                result.PackageId = metadata.Element(ns + "id")?.Value;
                result.PackageVersion = metadata.Element(ns + "version")?.Value;
                result.Description = metadata.Element(ns + "description")?.Value;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to parse .nuspec file: {ex.Message}");
            return result;
        }

        // Check for required content structure
        var contentPath = Path.Combine(extractedPath, "contentFiles", "any", "any");
        if (!Directory.Exists(contentPath))
        {
            // Try alternate structure (direct content)
            contentPath = extractedPath;
        }

        // Check for CodeCSharp or CodePython folder
        var csharpPath = FindDirectory(extractedPath, "CodeCSharp");
        var pythonPath = FindDirectory(extractedPath, "CodePython");

        if (csharpPath == null && pythonPath == null)
        {
            result.Warnings.Add("Neither 'CodeCSharp' nor 'CodePython' folder found. Package may not contain executable code.");
        }

        if (csharpPath != null)
        {
            result.HasCSharpCode = true;
            result.CSharpCodePath = csharpPath;

            // Check for main.cs
            var mainCs = Path.Combine(csharpPath, "main.cs");
            if (!File.Exists(mainCs))
            {
                result.Warnings.Add("'main.cs' not found in CodeCSharp folder.");
            }
        }

        if (pythonPath != null)
        {
            result.HasPythonCode = true;
            result.PythonCodePath = pythonPath;

            // Check for main.py
            var mainPy = Path.Combine(pythonPath, "main.py");
            if (!File.Exists(mainPy))
            {
                result.Warnings.Add("'main.py' not found in CodePython folder.");
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    /// <summary>
    /// Parses NuGet dependencies from the .nuspec file.
    /// </summary>
    /// <param name="extractedPath">The path where the package was extracted</param>
    /// <returns>List of dependency groups with their dependencies</returns>
    public async Task<List<NuGetDependencyGroup>> GetDependenciesFromNuSpecAsync(string extractedPath)
    {
        var result = new List<NuGetDependencyGroup>();

        // Check for .nuspec file
        var nuspecFiles = Directory.GetFiles(extractedPath, "*.nuspec", SearchOption.TopDirectoryOnly);
        if (nuspecFiles.Length == 0)
        {
            return result;
        }

        try
        {
            var nuspecPath = nuspecFiles[0];
            var nuspecXml = await File.ReadAllTextAsync(nuspecPath);
            var doc = XDocument.Parse(nuspecXml);

            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var metadata = doc.Root?.Element(ns + "metadata");
            var dependencies = metadata?.Element(ns + "dependencies");

            if (dependencies == null)
            {
                return result;
            }

            // Handle dependency groups (framework-specific)
            var groups = dependencies.Elements(ns + "group");
            foreach (var group in groups)
            {
                var dependencyGroup = new NuGetDependencyGroup
                {
                    TargetFramework = group.Attribute("targetFramework")?.Value
                };

                foreach (var dep in group.Elements(ns + "dependency"))
                {
                    var nugetDep = new NuGetDependency
                    {
                        PackageId = dep.Attribute("id")?.Value ?? string.Empty,
                        Version = dep.Attribute("version")?.Value ?? string.Empty,
                        Exclude = dep.Attribute("exclude")?.Value
                    };

                    if (!string.IsNullOrEmpty(nugetDep.PackageId))
                    {
                        dependencyGroup.Dependencies.Add(nugetDep);
                    }
                }

                if (dependencyGroup.Dependencies.Count > 0)
                {
                    result.Add(dependencyGroup);
                }
            }

            // Handle top-level dependencies (not in a group - older format)
            var topLevelDeps = dependencies.Elements(ns + "dependency")
                .Where(d => d.Parent?.Name.LocalName == "dependencies");
            
            if (topLevelDeps.Any())
            {
                var defaultGroup = new NuGetDependencyGroup();
                foreach (var dep in topLevelDeps)
                {
                    var nugetDep = new NuGetDependency
                    {
                        PackageId = dep.Attribute("id")?.Value ?? string.Empty,
                        Version = dep.Attribute("version")?.Value ?? string.Empty,
                        Exclude = dep.Attribute("exclude")?.Value
                    };

                    if (!string.IsNullOrEmpty(nugetDep.PackageId))
                    {
                        defaultGroup.Dependencies.Add(nugetDep);
                    }
                }

                if (defaultGroup.Dependencies.Count > 0)
                {
                    result.Add(defaultGroup);
                }
            }
        }
        catch
        {
            // Return empty list on parse errors
        }

        return result;
    }

    /// <summary>
    /// Gets the best matching target framework from available dependency groups.
    /// </summary>
    /// <param name="groups">Available dependency groups</param>
    /// <param name="preferredFramework">Preferred target framework (e.g., "net10.0")</param>
    /// <returns>The best matching dependency group, or null if none found</returns>
    public NuGetDependencyGroup? GetBestMatchingDependencyGroup(
        List<NuGetDependencyGroup> groups,
        string preferredFramework)
    {
        if (groups == null || groups.Count == 0)
            return null;

        // Try exact match first
        var exactMatch = groups.FirstOrDefault(g =>
            g.TargetFramework?.Equals(preferredFramework, StringComparison.OrdinalIgnoreCase) == true);
        if (exactMatch != null)
            return exactMatch;

        // Try to find a compatible framework (prefer newer)
        // Common frameworks in order of preference for net10.0
        var compatibleFrameworks = new[]
        {
            "net10.0", "net9.0", "net8.0", "net7.0", "net6.0",
            "netstandard2.1", "netstandard2.0"
        };

        foreach (var fw in compatibleFrameworks)
        {
            var match = groups.FirstOrDefault(g =>
                g.TargetFramework?.Equals(fw, StringComparison.OrdinalIgnoreCase) == true);
            if (match != null)
                return match;
        }

        // Fall back to first group (may be framework-agnostic)
        return groups.FirstOrDefault(g => g.Dependencies.Count > 0);
    }

    /// <summary>
    /// Reads the configuration.json file from an extracted package.
    /// </summary>
    /// <param name="extractedPath">The path where the package was extracted</param>
    /// <returns>The job configuration, or a default configuration if not found</returns>
    public async Task<JobConfiguration> GetConfigurationAsync(string extractedPath)
    {
        var configPath = FindFile(extractedPath, "configuration.json");

        if (configPath == null)
        {
            // Return default configuration - auto-detect language
            var config = new JobConfiguration();

            // Auto-detect language based on available code folders
            var csharpPath = FindDirectory(extractedPath, "CodeCSharp");
            var pythonPath = FindDirectory(extractedPath, "CodePython");

            if (csharpPath != null)
            {
                config.SelectedLanguage = "CSharp";
            }
            else if (pythonPath != null)
            {
                config.SelectedLanguage = "Python";
            }

            return config;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<JobConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return config ?? new JobConfiguration();
        }
        catch
        {
            return new JobConfiguration();
        }
    }

    /// <summary>
    /// Finds a directory by name, searching recursively.
    /// </summary>
    private string? FindDirectory(string basePath, string directoryName)
    {
        // First check direct children
        var directPath = Path.Combine(basePath, directoryName);
        if (Directory.Exists(directPath))
            return directPath;

        // Check in contentFiles structure
        var contentPath = Path.Combine(basePath, "contentFiles", "any", "any", directoryName);
        if (Directory.Exists(contentPath))
            return contentPath;

        // Search recursively
        try
        {
            var directories = Directory.GetDirectories(basePath, directoryName, SearchOption.AllDirectories);
            return directories.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds a file by name, searching recursively.
    /// </summary>
    private string? FindFile(string basePath, string fileName)
    {
        // First check direct
        var directPath = Path.Combine(basePath, fileName);
        if (File.Exists(directPath))
            return directPath;

        // Search recursively
        try
        {
            var files = Directory.GetFiles(basePath, fileName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the path to the code folder based on the selected language.
    /// </summary>
    /// <param name="extractedPath">The path where the package was extracted</param>
    /// <param name="language">The selected language (CSharp or Python)</param>
    /// <returns>The path to the code folder, or null if not found</returns>
    public string? GetCodeFolderPath(string extractedPath, string language)
    {
        var folderName = language.Equals("Python", StringComparison.OrdinalIgnoreCase)
            ? "CodePython"
            : "CodeCSharp";

        return FindDirectory(extractedPath, folderName);
    }
}

/// <summary>
/// Result of package validation.
/// </summary>
public class PackageValidationResult
{
    /// <summary>
    /// Whether the package is valid for execution.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// The package ID from .nuspec.
    /// </summary>
    public string? PackageId { get; set; }

    /// <summary>
    /// The package version from .nuspec.
    /// </summary>
    public string? PackageVersion { get; set; }

    /// <summary>
    /// The package description from .nuspec.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the package contains C# code.
    /// </summary>
    public bool HasCSharpCode { get; set; }

    /// <summary>
    /// Path to the C# code folder.
    /// </summary>
    public string? CSharpCodePath { get; set; }

    /// <summary>
    /// Whether the package contains Python code.
    /// </summary>
    public bool HasPythonCode { get; set; }

    /// <summary>
    /// Path to the Python code folder.
    /// </summary>
    public string? PythonCodePath { get; set; }

    /// <summary>
    /// Validation errors that prevent execution.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Validation warnings (package may still execute).
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}
