using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        /// Optional paths to the per-environment appsettings overlays, keyed by canonical environment name.
        /// </summary>
        public Dictionary<string, string> EnvironmentAppSettingsPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>
        /// Identifier for this build, used to scope the temp folders and to correlate
        /// log lines produced by concurrent builds. Surfaced in the UI for support.
        /// </summary>
        public string BuildId { get; set; } = string.Empty;

        /// <summary>
        /// The build-scoped folder holding the produced package, or null if none was created.
        /// </summary>
        public string? OutputFolder { get; set; }
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
    /// The NuGet package-id grammar. Excludes '/', '\\', ':' and '..', so a value matching
    /// this pattern cannot escape the folder it is combined with.
    /// </summary>
    private static readonly Regex PackageIdPattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.Compiled);

    private static readonly TimeSpan TempRetention = TimeSpan.FromHours(24);
    private static readonly object TempSweepGate = new();
    private static DateTime _lastTempSweepUtc = DateTime.MinValue;

    public NuGetPackageBuilderService()
    {
        SweepStaleOutputFolders();
    }

    /// <summary>
    /// Creates a NuGet package from code files.
    /// </summary>
    /// <param name="config">The package build configuration.</param>
    /// <returns>The result of the package build operation.</returns>
    public async Task<PackageBuildResult> BuildPackageAsync(PackageBuildConfiguration config)
    {
        var result = new PackageBuildResult();

        // Validate before any directory is created: PackageId reaches both the nuspec
        // filename and the nupkg filename.
        if (!PackageIdPattern.IsMatch(config.PackageId))
        {
            result.Success = false;
            result.ErrorMessage =
                $"Invalid package id '{config.PackageId}'. Use letters, digits, dot, underscore " +
                "and hyphen only, starting with a letter or digit, maximum 100 characters.";
            result.Logs.Add(result.ErrorMessage);
            return result;
        }

        // Not a parseable NuGetVersion: the patch component overflows Int32. Intentional —
        // the Agent unzips this artifact and never resolves it through a NuGet client.
        var version = config.Version ?? $"1.0.{DateTime.UtcNow:yyyyMMddHHmmss}";
        result.Version = version;

        var buildId = Guid.NewGuid().ToString("N");
        result.BuildId = buildId;

        var tempFolder = Path.Combine(Path.GetTempPath(), "NuGetBuild", buildId);
        var outputFolder = Path.Combine(Path.GetTempPath(), "NuGetPackages", buildId);
        result.OutputFolder = outputFolder;

        try
        {
            // Ensure directories exist
            Directory.CreateDirectory(tempFolder);
            Directory.CreateDirectory(outputFolder);

            result.Logs.Add($"[build {buildId}] Building package {config.PackageId} v{version}");

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

            // Security: compiled binaries are rejected per file inside CopyFileAsync, by
            // inspecting the PE header rather than the extension. A stray bin/ or obj/
            // folder under CodeRootPath is harmless — no copy loop ever reaches it.

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
                var destPath = Path.Combine(rootContentFolder, Configuration.JobEnvironments.BaseFileName);
                await CopyFileAsync(config.AppSettingsPath, destPath);
                result.IncludedFiles.Add(Configuration.JobEnvironments.BaseFileName);
                result.Logs.Add($"Added {Configuration.JobEnvironments.BaseFileName}");
            }

            foreach (var environment in Configuration.JobEnvironments.All)
            {
                if (!config.EnvironmentAppSettingsPaths.TryGetValue(environment, out var sourcePath) || !File.Exists(sourcePath))
                {
                    continue;
                }

                var fileName = Configuration.JobEnvironments.GetFileName(environment);
                await CopyFileAsync(sourcePath, Path.Combine(rootContentFolder, fileName));
                result.IncludedFiles.Add(fileName);
                result.Logs.Add($"Added {fileName}");
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

            // Refuse to ship a package with no code. Reproduces and diagnoses I-001.
            var csharpCount = Directory.GetFiles(csharpContentFolder, "*.cs").Length;
            var pythonCount = Directory.GetFiles(pythonContentFolder, "*.py").Length;

            if (csharpCount == 0 && pythonCount == 0)
            {
                var strays = Directory.Exists(config.CodeRootPath)
                    ? Directory.GetFiles(config.CodeRootPath, "*.cs")
                        .Concat(Directory.GetFiles(config.CodeRootPath, "*.py"))
                        .Select(Path.GetFileName)
                        .ToArray()
                    : Array.Empty<string>();

                result.Success = false;
                result.ErrorMessage = strays.Length > 0
                    ? "No code was packaged. Found code files directly under the job root " +
                      $"({string.Join(", ", strays)}); they must live in CodeCSharp or CodePython."
                    : "No code was packaged. Expected at least one .cs file in CodeCSharp " +
                      "or one .py file in CodePython.";
                result.Logs.Add(result.ErrorMessage);
                return result;
            }

            if (csharpCount > 0 && !File.Exists(Path.Combine(csharpContentFolder, "main.cs")))
            {
                result.Logs.Add("Note: no main.cs in CodeCSharp. All .cs files are compiled together, " +
                    "so the filename is not significant.");
            }

            // Load or use provided dependencies
            List<PackageDependency> dependencies;
            if (config.Dependencies != null)
            {
                dependencies = config.Dependencies;
                result.Logs.Add($"Dependencies supplied by caller: {dependencies.Count}");
            }
            else
            {
                var dependenciesPath = config.DependenciesFilePath ?? Path.Combine(csharpFolder, "dependencies.json");
                if (File.Exists(dependenciesPath))
                {
                    dependencies = await LoadDependenciesAsync(dependenciesPath);
                    result.Logs.Add($"Loaded {dependencies.Count} dependencies from '{dependenciesPath}'");
                }
                else
                {
                    dependencies = new List<PackageDependency>();
                    result.Logs.Add($"Dependencies file not found at '{dependenciesPath}'; continuing with defaults only");
                }
            }

            foreach (var dep in dependencies)
            {
                result.Logs.Add($"  dependency: {dep.Id} {dep.Version}");
            }

            // Ensure default dependencies are included
            foreach (var defaultDep in DefaultDependencies)
            {
                if (!dependencies.Any(d => d.Id.Equals(defaultDep.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    dependencies.Add(defaultDep);
                    result.Logs.Add($"  default injected: {defaultDep.Id} {defaultDep.Version}");
                }
                else
                {
                    result.Logs.Add($"  default skipped, already declared by the job: {defaultDep.Id}");
                }
            }

            // A hand-edited dependencies.json can carry a floating version that never passed
            // through ExtractDependenciesFromProjectAsync. The nuspec cannot express one.
            var floating = dependencies.Where(d => d.Version.Contains('*')).ToList();
            if (floating.Count > 0)
            {
                throw new PackageBuildException(
                    "Floating versions are not permitted in a job package: " +
                    string.Join(", ", floating.Select(d => $"{d.Id} {d.Version}")));
            }

            result.Logs.Add($"Using {dependencies.Count} dependencies");

            // Create the .nuspec file
            var nuspecPath = Path.Combine(tempFolder, $"{config.PackageId}.nuspec");
            await CreateNuspecFileAsync(nuspecPath, config.PackageId, version, config.Description, config.Authors, dependencies);

            // Create the .nupkg file. The output folder is build-scoped, so no collision
            // is possible and no pre-delete is needed.
            var nupkgPath = Path.Combine(outputFolder, $"{config.PackageId}.{version}.nupkg");

            // Create the NuGet package (which is a ZIP file with .nupkg extension)
            ZipFile.CreateFromDirectory(tempFolder, nupkgPath);

            result.Success = true;
            result.PackagePath = nupkgPath;
            result.FileName = Path.GetFileName(nupkgPath);
            result.Logs.Add($"Package created: {result.FileName}");

            return result;
        }
        catch (PackageBuildException ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Logs.Add($"Build failed: {ex.Message}");
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
            CleanupBuild(result);

            // Throw rather than return null: the caller would otherwise replace the real
            // reason with a generic message. The build id is the support correlation handle.
            throw new PackageBuildException(
                $"{result.ErrorMessage ?? "Package build failed."} (build {result.BuildId})");
        }

        MemoryStream? memoryStream = null;
        try
        {
            memoryStream = new MemoryStream();
            await using (var fileStream = File.OpenRead(result.PackagePath))
            {
                await fileStream.CopyToAsync(memoryStream);
            }
            memoryStream.Position = 0;

            return (memoryStream, result.FileName!, result.Version!);
        }
        catch
        {
            memoryStream?.Dispose();
            throw;
        }
        finally
        {
            CleanupBuild(result);
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
        catch (JsonException ex)
        {
            throw new PackageBuildException(
                $"Failed to parse dependencies file '{dependenciesFilePath}': {ex.Message}", ex);
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
            "GitHub.Copilot.SDK",
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

                // Exclusion runs before validation: the template legitimately carries
                // floating references to host-supplied packages.
                var shouldExclude = excludePatterns.Any(pattern =>
                    id.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                    id.Equals(pattern, StringComparison.OrdinalIgnoreCase));

                if (shouldExclude)
                    continue;

                // Covers "*", "1.*" and "1.2.*".
                if (version.Contains('*'))
                {
                    throw new PackageBuildException(
                        $"Package '{id}' uses the floating version '{version}'. Floating versions " +
                        "cannot be written to a job package. Pin an exact version in the project file, " +
                        "or add the package to the exclusion list if the host supplies it.");
                }

                dependencies.Add(new PackageDependency { Id = id, Version = version });
            }
        }
        catch (System.Xml.XmlException ex)
        {
            throw new PackageBuildException(
                $"Failed to parse project file '{projectFilePath}': {ex.Message}", ex);
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

        // 'MZ' is the DOS header shared by every PE image, so this catches a .dll or .exe
        // renamed to .json/.txt/.cs/.py. No legitimate source or config file starts with it.
        var header = new byte[2];
        if (await sourceStream.ReadAsync(header.AsMemory(0, 2)) == 2 &&
            header[0] == 0x4D && header[1] == 0x5A)
        {
            throw new PackageBuildException(
                $"'{Path.GetFileName(sourcePath)}' is a compiled binary renamed to " +
                $"'{Path.GetExtension(sourcePath)}'. Compiled binaries cannot be packaged; " +
                "declare a NuGet dependency instead.");
        }

        sourceStream.Position = 0;
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

    /// <summary>
    /// Deletes the build-scoped output folder produced by <see cref="BuildPackageAsync"/>.
    /// </summary>
    private static void CleanupBuild(PackageBuildResult result)
    {
        try
        {
            if (!string.IsNullOrEmpty(result.OutputFolder) && Directory.Exists(result.OutputFolder))
            {
                Directory.Delete(result.OutputFolder, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Deletes build-scoped output folders older than 24 hours. BuildPackageAsync leaves the
    /// package on disk for its caller to own, so this bounds the leak without changing that contract.
    /// </summary>
    private static void SweepStaleOutputFolders()
    {
        lock (TempSweepGate)
        {
            if (DateTime.UtcNow - _lastTempSweepUtc < TempRetention)
            {
                return;
            }
            _lastTempSweepUtc = DateTime.UtcNow;
        }

        try
        {
            var root = Path.Combine(Path.GetTempPath(), "NuGetPackages");
            if (!Directory.Exists(root))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - TempRetention;
            foreach (var folder in Directory.GetDirectories(root))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(folder) < cutoff)
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
                catch
                {
                    // A folder in use by a concurrent build is skipped and swept next time.
                }
            }
        }
        catch
        {
            // Sweeping is best-effort and must never fail construction.
        }
    }
}
