using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for building NuGet packages from job code.
/// This is a shared service that can be used by both the JobCreatorTemplate and other components.
/// </summary>
public class NuGetPackageBuilderService
{
    /// <summary>
    /// Configuration for package building.
    /// </summary>
    public class PackageBuildConfiguration
    {
        /// <summary>
        /// The root path where code files are located.
        /// </summary>
        public required string CodeRootPath { get; set; }

        /// <summary>
        /// The package identifier.
        /// </summary>
        public string PackageId { get; set; } = "BlazorDataOrchestrator.Job";

        /// <summary>
        /// The package version (auto-generated if not provided).
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// The package description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The package authors.
        /// </summary>
        public string? Authors { get; set; }

        /// <summary>
        /// Optional path to appsettings.json to include in the package.
        /// </summary>
        public string? AppSettingsPath { get; set; }

        /// <summary>
        /// Optional path to appsettingsProduction.json to include in the package.
        /// </summary>
        public string? AppSettingsProductionPath { get; set; }

        /// <summary>
        /// Path to dependencies.json file (optional).
        /// </summary>
        public string? DependenciesFilePath { get; set; }

        /// <summary>
        /// Custom dependencies to include in the package.
        /// </summary>
        public List<PackageDependency>? Dependencies { get; set; }
    }

    /// <summary>
    /// Result of package building.
    /// </summary>
    public class PackageBuildResult
    {
        public bool Success { get; set; }
        public string? PackagePath { get; set; }
        public string? FileName { get; set; }
        public string? Version { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> IncludedFiles { get; set; } = new();
        public List<string> Logs { get; set; } = new();
    }

    /// <summary>
    /// Default NuGet dependencies for job packages.
    /// </summary>
    public static readonly List<PackageDependency> DefaultDependencies = new()
    {
        new() { Id = "Microsoft.EntityFrameworkCore", Version = "10.0.0" },
        new() { Id = "Microsoft.EntityFrameworkCore.SqlServer", Version = "10.0.0" },
        new() { Id = "Azure.Data.Tables", Version = "12.9.1" }
    };

    /// <summary>
    /// Creates a NuGet package from code files.
    /// </summary>
    /// <param name="config">The package build configuration.</param>
    /// <returns>The result of the package build operation.</returns>
    public async Task<PackageBuildResult> BuildPackageAsync(PackageBuildConfiguration config)
    {
        var result = new PackageBuildResult();

        // Generate unique version if not provided
        var version = config.Version ?? $"1.0.{DateTime.Now:yyyyMMddHHmmss}";
        result.Version = version;

        var tempFolder = Path.Combine(Path.GetTempPath(), "NuGetBuild", Guid.NewGuid().ToString());
        var outputFolder = Path.Combine(Path.GetTempPath(), "NuGetPackages");

        try
        {
            // Ensure directories exist
            Directory.CreateDirectory(tempFolder);
            Directory.CreateDirectory(outputFolder);

            result.Logs.Add($"Building package {config.PackageId} v{version}");

            // Create the package structure
            var rootContentFolder = Path.Combine(tempFolder, "contentFiles", "any", "any");
            var csharpContentFolder = Path.Combine(rootContentFolder, "CodeCSharp");
            var pythonContentFolder = Path.Combine(rootContentFolder, "CodePython");

            Directory.CreateDirectory(rootContentFolder);
            Directory.CreateDirectory(csharpContentFolder);
            Directory.CreateDirectory(pythonContentFolder);

            var allCodeFiles = new List<string>();

            // Copy code files from source
            var csharpFolder = Path.Combine(config.CodeRootPath, "CodeCSharp");
            var pythonFolder = Path.Combine(config.CodeRootPath, "CodePython");

            // Copy JSON files from the root Code folder
            if (Directory.Exists(config.CodeRootPath))
            {
                var rootJsonFiles = Directory.GetFiles(config.CodeRootPath, "*.json");
                foreach (var file in rootJsonFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (!fileName.Equals("configuration.json", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(rootContentFolder, fileName);
                        await CopyFileAsync(file, destPath);
                        result.IncludedFiles.Add(fileName);
                        result.Logs.Add($"Added root config file: {fileName}");
                    }
                }
            }

            // Copy appsettings files if provided
            if (!string.IsNullOrEmpty(config.AppSettingsPath) && File.Exists(config.AppSettingsPath))
            {
                var destPath = Path.Combine(rootContentFolder, "appsettings.json");
                await CopyFileAsync(config.AppSettingsPath, destPath);
                result.IncludedFiles.Add("appsettings.json");
                result.Logs.Add("Added appsettings.json");
            }

            if (!string.IsNullOrEmpty(config.AppSettingsProductionPath) && File.Exists(config.AppSettingsProductionPath))
            {
                var destPath = Path.Combine(rootContentFolder, "appsettingsProduction.json");
                await CopyFileAsync(config.AppSettingsProductionPath, destPath);
                result.IncludedFiles.Add("appsettingsProduction.json");
                result.Logs.Add("Added appsettingsProduction.json");
            }

            // Copy C# files
            if (Directory.Exists(csharpFolder))
            {
                var csharpFiles = Directory.GetFiles(csharpFolder, "*.cs");
                foreach (var file in csharpFiles)
                {
                    var destPath = Path.Combine(csharpContentFolder, Path.GetFileName(file));
                    await CopyFileAsync(file, destPath);
                    allCodeFiles.Add(file);
                    result.IncludedFiles.Add($"CodeCSharp/{Path.GetFileName(file)}");
                    result.Logs.Add($"Added C# file: {Path.GetFileName(file)}");
                }

                // Copy JSON configuration files from CodeCSharp
                var csharpJsonFiles = Directory.GetFiles(csharpFolder, "*.json");
                foreach (var file in csharpJsonFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (!fileName.Equals("configuration.json", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(csharpContentFolder, fileName);
                        await CopyFileAsync(file, destPath);
                        result.IncludedFiles.Add($"CodeCSharp/{fileName}");
                        result.Logs.Add($"Added C# config: {fileName}");
                    }
                }
            }

            // Copy Python files
            if (Directory.Exists(pythonFolder))
            {
                var pythonFiles = Directory.GetFiles(pythonFolder, "*.py");
                foreach (var file in pythonFiles)
                {
                    var destPath = Path.Combine(pythonContentFolder, Path.GetFileName(file));
                    await CopyFileAsync(file, destPath);
                    result.IncludedFiles.Add($"CodePython/{Path.GetFileName(file)}");
                    result.Logs.Add($"Added Python file: {Path.GetFileName(file)}");
                }

                // Copy txt files (requirements.txt)
                var txtFiles = Directory.GetFiles(pythonFolder, "*.txt");
                foreach (var file in txtFiles)
                {
                    var destPath = Path.Combine(pythonContentFolder, Path.GetFileName(file));
                    await CopyFileAsync(file, destPath);
                    result.IncludedFiles.Add($"CodePython/{Path.GetFileName(file)}");
                    result.Logs.Add($"Added Python txt: {Path.GetFileName(file)}");
                }

                // Copy JSON configuration files from CodePython
                var pythonJsonFiles = Directory.GetFiles(pythonFolder, "*.json");
                foreach (var file in pythonJsonFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (!fileName.Equals("configuration.json", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(pythonContentFolder, fileName);
                        await CopyFileAsync(file, destPath);
                        result.IncludedFiles.Add($"CodePython/{fileName}");
                        result.Logs.Add($"Added Python config: {fileName}");
                    }
                }
            }

            // Load or use provided dependencies
            var dependencies = config.Dependencies ?? await LoadDependenciesAsync(config.DependenciesFilePath ?? Path.Combine(csharpFolder, "dependencies.json"));

            // Ensure default dependencies are included
            foreach (var defaultDep in DefaultDependencies)
            {
                if (!dependencies.Any(d => d.Id.Equals(defaultDep.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    dependencies.Add(defaultDep);
                }
            }

            result.Logs.Add($"Using {dependencies.Count} dependencies");

            // Create the .nuspec file
            var nuspecPath = Path.Combine(tempFolder, $"{config.PackageId}.nuspec");
            await CreateNuspecFileAsync(nuspecPath, config.PackageId, version, config.Description, config.Authors, dependencies);

            // Create the .nupkg file
            var nupkgPath = Path.Combine(outputFolder, $"{config.PackageId}.{version}.nupkg");

            // Remove existing file if present
            if (File.Exists(nupkgPath))
            {
                File.Delete(nupkgPath);
            }

            // Create the NuGet package (which is a ZIP file with .nupkg extension)
            ZipFile.CreateFromDirectory(tempFolder, nupkgPath);

            result.Success = true;
            result.PackagePath = nupkgPath;
            result.FileName = Path.GetFileName(nupkgPath);
            result.Logs.Add($"Package created: {result.FileName}");

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Logs.Add($"Error: {ex.Message}");
            return result;
        }
        finally
        {
            // Cleanup temp folder
            try
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Creates a NuGet package and returns it as a MemoryStream.
    /// </summary>
    /// <param name="config">The package build configuration.</param>
    /// <returns>Tuple containing the package stream, filename, and version.</returns>
    public async Task<(MemoryStream PackageStream, string FileName, string Version)?> BuildPackageAsStreamAsync(PackageBuildConfiguration config)
    {
        var result = await BuildPackageAsync(config);

        if (!result.Success || string.IsNullOrEmpty(result.PackagePath))
        {
            return null;
        }

        try
        {
            var memoryStream = new MemoryStream();
            await using (var fileStream = File.OpenRead(result.PackagePath))
            {
                await fileStream.CopyToAsync(memoryStream);
            }
            memoryStream.Position = 0;

            // Cleanup the temp file
            CleanupPackage(result.PackagePath);

            return (memoryStream, result.FileName!, result.Version!);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads dependencies from a dependencies.json file.
    /// </summary>
    public async Task<List<PackageDependency>> LoadDependenciesAsync(string dependenciesFilePath)
    {
        var dependencies = new List<PackageDependency>();

        if (!File.Exists(dependenciesFilePath))
        {
            return dependencies;
        }

        try
        {
            var json = await File.ReadAllTextAsync(dependenciesFilePath);
            var config = JsonSerializer.Deserialize<DependenciesConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config?.Dependencies != null)
            {
                dependencies.AddRange(config.Dependencies);
            }
        }
        catch
        {
            // Return empty list on parse errors
        }

        return dependencies;
    }

    /// <summary>
    /// Extracts NuGet package references from a .csproj file.
    /// </summary>
    /// <param name="projectFilePath">Path to the .csproj file.</param>
    /// <param name="excludePatterns">Package ID patterns to exclude.</param>
    /// <returns>List of extracted dependencies.</returns>
    public async Task<List<PackageDependency>> ExtractDependenciesFromProjectAsync(
        string projectFilePath,
        string[]? excludePatterns = null)
    {
        var dependencies = new List<PackageDependency>();

        if (!File.Exists(projectFilePath))
        {
            return dependencies;
        }

        excludePatterns ??= new[]
        {
            "Aspire.",
            "Radzen.Blazor",
            "SimpleBlazorMonaco",
            "Microsoft.CodeAnalysis",
            "Microsoft.Extensions.AI",
            "Azure.AI.OpenAI"
        };

        try
        {
            var xml = await File.ReadAllTextAsync(projectFilePath);
            var doc = XDocument.Parse(xml);

            var packageReferences = doc.Descendants("PackageReference");

            foreach (var packageRef in packageReferences)
            {
                var id = packageRef.Attribute("Include")?.Value;
                var version = packageRef.Attribute("Version")?.Value;

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version))
                    continue;

                // Skip wildcard versions
                if (version == "*")
                    continue;

                // Check exclude patterns
                var shouldExclude = excludePatterns.Any(pattern =>
                    id.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                    id.Equals(pattern, StringComparison.OrdinalIgnoreCase));

                if (shouldExclude)
                    continue;

                dependencies.Add(new PackageDependency { Id = id, Version = version });
            }
        }
        catch
        {
            // Return empty list on parse errors
        }

        return dependencies;
    }

    /// <summary>
    /// Saves dependencies to a dependencies.json file.
    /// </summary>
    public async Task SaveDependenciesAsync(string filePath, List<PackageDependency> dependencies)
    {
        var config = new DependenciesConfig { Dependencies = dependencies };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        await File.WriteAllTextAsync(filePath, json);
    }

    private async Task CreateNuspecFileAsync(
        string nuspecPath,
        string packageId,
        string version,
        string? description,
        string? authors,
        List<PackageDependency> dependencies)
    {
        var xmlns = XNamespace.Get("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd");

        // Build dependency elements
        var dependencyElements = dependencies.Select(dep =>
            new XElement(xmlns + "dependency",
                new XAttribute("id", dep.Id),
                new XAttribute("version", dep.Version))).ToArray();

        var nuspec = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(xmlns + "package",
                new XElement(xmlns + "metadata",
                    new XElement(xmlns + "id", packageId),
                    new XElement(xmlns + "version", version),
                    new XElement(xmlns + "authors", authors ?? "BlazorDataOrchestrator"),
                    new XElement(xmlns + "description", description ?? $"Job package created from BlazorDataOrchestrator on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"),
                    new XElement(xmlns + "requireLicenseAcceptance", "false"),
                    new XElement(xmlns + "contentFiles",
                        // Root JSON config files
                        new XElement(xmlns + "files",
                            new XAttribute("include", "any/any/*.json"),
                            new XAttribute("buildAction", "Content"),
                            new XAttribute("copyToOutput", "true")),
                        // C# files - compile as code
                        new XElement(xmlns + "files",
                            new XAttribute("include", "any/any/CodeCSharp/*.cs"),
                            new XAttribute("buildAction", "Compile")),
                        // C# JSON config files
                        new XElement(xmlns + "files",
                            new XAttribute("include", "any/any/CodeCSharp/*.json"),
                            new XAttribute("buildAction", "Content"),
                            new XAttribute("copyToOutput", "true")),
                        // Python files - include as content
                        new XElement(xmlns + "files",
                            new XAttribute("include", "any/any/CodePython/*.py"),
                            new XAttribute("buildAction", "Content"),
                            new XAttribute("copyToOutput", "true")),
                        // Python txt files (requirements.txt, etc.)
                        new XElement(xmlns + "files",
                            new XAttribute("include", "any/any/CodePython/*.txt"),
                            new XAttribute("buildAction", "Content"),
                            new XAttribute("copyToOutput", "true")),
                        // Python JSON config files
                        new XElement(xmlns + "files",
                            new XAttribute("include", "any/any/CodePython/*.json"),
                            new XAttribute("buildAction", "Content"),
                            new XAttribute("copyToOutput", "true"))),
                    new XElement(xmlns + "dependencies",
                        new XElement(xmlns + "group",
                            new XAttribute("targetFramework", "net10.0"),
                            dependencyElements)))));

        await using var stream = new FileStream(nuspecPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await Task.Run(() => nuspec.Save(stream));
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath)
    {
        await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await sourceStream.CopyToAsync(destStream);
    }

    /// <summary>
    /// Cleans up a package file.
    /// </summary>
    public void CleanupPackage(string packagePath)
    {
        try
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
