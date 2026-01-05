using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services
{
    /// <summary>
    /// Service for creating NuGet packages from the job code.
    /// </summary>
    public class NuGetPackageService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<NuGetPackageService> _logger;

        public NuGetPackageService(IWebHostEnvironment environment, ILogger<NuGetPackageService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        /// <summary>
        /// Represents a NuGet package dependency.
        /// </summary>
        public class PackageDependency
        {
            public string Id { get; set; } = "";
            public string Version { get; set; } = "";
        }

        /// <summary>
        /// Represents the dependencies configuration file structure.
        /// </summary>
        public class DependenciesConfig
        {
            public List<PackageDependency> Dependencies { get; set; } = new();
        }

        /// <summary>
        /// Extracts all NuGet package references from the project file and updates dependencies.json.
        /// </summary>
        /// <returns>The list of extracted dependencies.</returns>
        public async Task<List<PackageDependency>> ExtractAndSaveDependenciesFromProjectAsync()
        {
            var dependencies = new List<PackageDependency>();
            var projectFile = Path.Combine(_environment.ContentRootPath, "BlazorDataOrchestrator.JobCreatorTemplate.csproj");

            if (!File.Exists(projectFile))
            {
                _logger.LogWarning("Project file not found: {ProjectFile}", projectFile);
                return dependencies;
            }

            try
            {
                var xml = await File.ReadAllTextAsync(projectFile);
                var doc = XDocument.Parse(xml);

                // Find all PackageReference elements
                var packageReferences = doc.Descendants("PackageReference");

                foreach (var packageRef in packageReferences)
                {
                    var id = packageRef.Attribute("Include")?.Value;
                    var version = packageRef.Attribute("Version")?.Value;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                    {
                        // Skip wildcard versions and Aspire-specific packages (they're for orchestration, not the job)
                        if (version == "*") continue;
                        if (id.StartsWith("Aspire.", StringComparison.OrdinalIgnoreCase)) continue;

                        // Skip UI/designer-specific packages that won't be needed in the job
                        if (id.Equals("Radzen.Blazor", StringComparison.OrdinalIgnoreCase)) continue;
                        if (id.Equals("SimpleBlazorMonaco", StringComparison.OrdinalIgnoreCase)) continue;
                        if (id.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)) continue;
                        if (id.StartsWith("Microsoft.Extensions.AI", StringComparison.OrdinalIgnoreCase)) continue;
                        if (id.StartsWith("Azure.AI.OpenAI", StringComparison.OrdinalIgnoreCase)) continue;

                        dependencies.Add(new PackageDependency { Id = id, Version = version });
                        _logger.LogInformation("Extracted package dependency: {Id} v{Version}", id, version);
                    }
                }

                // Add essential dependencies that might come from referenced projects
                var essentialDependencies = new List<PackageDependency>
                {
                    new() { Id = "Microsoft.EntityFrameworkCore", Version = "10.0.0" },
                    new() { Id = "Microsoft.EntityFrameworkCore.SqlServer", Version = "10.0.0" },
                    new() { Id = "Azure.Data.Tables", Version = "12.9.1" }
                };

                foreach (var essential in essentialDependencies)
                {
                    if (!dependencies.Any(d => d.Id.Equals(essential.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        dependencies.Add(essential);
                    }
                }

                // Save to dependencies.json
                var csharpFolder = Path.Combine(_environment.ContentRootPath, "Code", "CodeCSharp");
                Directory.CreateDirectory(csharpFolder);

                var dependenciesFile = Path.Combine(csharpFolder, "dependencies.json");
                var config = new DependenciesConfig { Dependencies = dependencies };
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(dependenciesFile, json);

                _logger.LogInformation("Saved {Count} dependencies to dependencies.json", dependencies.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract dependencies from project file");
            }

            return dependencies;
        }

        /// <summary>
        /// Creates a NuGet package from the C# code files.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package version.</param>
        /// <param name="description">The package description.</param>
        /// <param name="authors">The package authors.</param>
        /// <returns>The path to the created .nupkg file.</returns>
        public async Task<string> CreatePackageAsync(
            string packageId = "BlazorDataOrchestrator.Job",
            string version = "1.0.0",
            string? description = null,
            string? authors = null)
        {
            var baseCodeFolder = Path.Combine(_environment.ContentRootPath, "Code");
            var csharpFolder = Path.Combine(baseCodeFolder, "CodeCSharp");
            var pythonFolder = Path.Combine(baseCodeFolder, "CodePython");
            var tempFolder = Path.Combine(Path.GetTempPath(), "NuGetBuild", Guid.NewGuid().ToString());
            var outputFolder = Path.Combine(Path.GetTempPath(), "NuGetPackages");

            try
            {
                // Ensure directories exist
                Directory.CreateDirectory(tempFolder);
                Directory.CreateDirectory(outputFolder);

                // Create the package structure for root Code folder files
                var rootContentFolder = Path.Combine(tempFolder, "contentFiles", "any", "any");
                Directory.CreateDirectory(rootContentFolder);

                // Create the package structure for C# files
                var csharpContentFolder = Path.Combine(tempFolder, "contentFiles", "any", "any", "CodeCSharp");
                Directory.CreateDirectory(csharpContentFolder);

                // Create the package structure for Python files
                var pythonContentFolder = Path.Combine(tempFolder, "contentFiles", "any", "any", "CodePython");
                Directory.CreateDirectory(pythonContentFolder);

                var allCodeFiles = new List<string>();

                // Copy JSON files from the root Code folder
                if (Directory.Exists(baseCodeFolder))
                {
                    var rootJsonFiles = Directory.GetFiles(baseCodeFolder, "*.json");
                    foreach (var file in rootJsonFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        if (!fileName.Equals("configuration.json", StringComparison.OrdinalIgnoreCase))
                        {
                            var destPath = Path.Combine(rootContentFolder, fileName);
                            await CopyFileAsync(file, destPath);
                            _logger.LogInformation("Added root config file to package: {FileName}", fileName);
                        }
                    }
                }

                // Copy appsettings.json and appsettingsProduction.json from project root
                var appSettingsFile = Path.Combine(_environment.ContentRootPath, "appsettings.json");
                if (File.Exists(appSettingsFile))
                {
                    var destPath = Path.Combine(rootContentFolder, "appsettings.json");
                    await CopyFileAsync(appSettingsFile, destPath);
                    _logger.LogInformation("Added appsettings.json to package");
                }

                var appSettingsProdFile = Path.Combine(_environment.ContentRootPath, "appsettingsProduction.json");
                if (File.Exists(appSettingsProdFile))
                {
                    var destPath = Path.Combine(rootContentFolder, "appsettingsProduction.json");
                    await CopyFileAsync(appSettingsProdFile, destPath);
                    _logger.LogInformation("Added appsettingsProduction.json to package");
                }

                // Copy all C# files from the CodeCSharp folder
                if (Directory.Exists(csharpFolder))
                {
                    var csharpFiles = Directory.GetFiles(csharpFolder, "*.cs");
                    foreach (var file in csharpFiles)
                    {
                        var destPath = Path.Combine(csharpContentFolder, Path.GetFileName(file));
                        await CopyFileAsync(file, destPath);
                        allCodeFiles.Add(file);
                        _logger.LogInformation("Added C# file to package: {FileName}", Path.GetFileName(file));
                    }

                    // Copy JSON configuration files from CodeCSharp if they exist
                    var csharpJsonFiles = Directory.GetFiles(csharpFolder, "*.json");
                    foreach (var file in csharpJsonFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        if (!fileName.Equals("configuration.json", StringComparison.OrdinalIgnoreCase))
                        {
                            var destPath = Path.Combine(csharpContentFolder, fileName);
                            await CopyFileAsync(file, destPath);
                            _logger.LogInformation("Added C# config file to package: {FileName}", fileName);
                        }
                    }
                }

                // Copy all Python files from the CodePython folder
                if (Directory.Exists(pythonFolder))
                {
                    var pythonFiles = Directory.GetFiles(pythonFolder, "*.py");
                    foreach (var file in pythonFiles)
                    {
                        var destPath = Path.Combine(pythonContentFolder, Path.GetFileName(file));
                        await CopyFileAsync(file, destPath);
                        _logger.LogInformation("Added Python file to package: {FileName}", Path.GetFileName(file));
                    }

                    // Copy txt files (like requirements.txt) from CodePython
                    var txtFiles = Directory.GetFiles(pythonFolder, "*.txt");
                    foreach (var file in txtFiles)
                    {
                        var destPath = Path.Combine(pythonContentFolder, Path.GetFileName(file));
                        await CopyFileAsync(file, destPath);
                        _logger.LogInformation("Added Python txt file to package: {FileName}", Path.GetFileName(file));
                    }

                    // Copy JSON configuration files from CodePython if they exist
                    var pythonJsonFiles = Directory.GetFiles(pythonFolder, "*.json");
                    foreach (var file in pythonJsonFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        if (!fileName.Equals("configuration.json", StringComparison.OrdinalIgnoreCase))
                        {
                            var destPath = Path.Combine(pythonContentFolder, fileName);
                            await CopyFileAsync(file, destPath);
                            _logger.LogInformation("Added Python config file to package: {FileName}", fileName);
                        }
                    }
                }

                // Load custom dependencies from dependencies.json if it exists
                var dependencies = await LoadDependenciesAsync(csharpFolder);

                // Create the .nuspec file
                var nuspecPath = Path.Combine(tempFolder, $"{packageId}.nuspec");
                await CreateNuspecFileAsync(nuspecPath, packageId, version, description, authors, allCodeFiles.ToArray(), dependencies);

                // Create the .nupkg file
                var nupkgPath = Path.Combine(outputFolder, $"{packageId}.{version}.nupkg");
                
                // Remove existing file if present
                if (File.Exists(nupkgPath))
                {
                    File.Delete(nupkgPath);
                }

                // Create the NuGet package (which is a ZIP file with .nupkg extension)
                ZipFile.CreateFromDirectory(tempFolder, nupkgPath);

                _logger.LogInformation("Created NuGet package: {PackagePath}", nupkgPath);

                return nupkgPath;
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp folder: {TempFolder}", tempFolder);
                }
            }
        }

        private async Task<List<PackageDependency>> LoadDependenciesAsync(string csharpFolder)
        {
            var dependencies = new List<PackageDependency>();
            var dependenciesFile = Path.Combine(csharpFolder, "dependencies.json");

            if (File.Exists(dependenciesFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(dependenciesFile);
                    var config = JsonSerializer.Deserialize<DependenciesConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (config?.Dependencies != null)
                    {
                        dependencies.AddRange(config.Dependencies);
                        _logger.LogInformation("Loaded {Count} custom dependencies from dependencies.json", config.Dependencies.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load dependencies.json, using default dependencies");
                }
            }
            else
            {
                _logger.LogInformation("No dependencies.json found, using default dependencies");
            }

            // Add default dependencies if not already present
            var defaultDependencies = new List<PackageDependency>
            {
                new() { Id = "Microsoft.EntityFrameworkCore", Version = "10.0.0" },
                new() { Id = "Microsoft.EntityFrameworkCore.SqlServer", Version = "10.0.0" },
                new() { Id = "Azure.Data.Tables", Version = "12.9.1" }
            };

            foreach (var defaultDep in defaultDependencies)
            {
                if (!dependencies.Any(d => d.Id.Equals(defaultDep.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    dependencies.Add(defaultDep);
                }
            }

            return dependencies;
        }

        private async Task CreateNuspecFileAsync(
            string nuspecPath,
            string packageId,
            string version,
            string? description,
            string? authors,
            string[] codeFiles,
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
        /// Gets the bytes of a NuGet package file.
        /// </summary>
        /// <param name="packagePath">The path to the .nupkg file.</param>
        /// <returns>The file bytes.</returns>
        public async Task<byte[]> GetPackageBytesAsync(string packagePath)
        {
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("Package file not found.", packagePath);
            }

            return await File.ReadAllBytesAsync(packagePath);
        }

        /// <summary>
        /// Cleans up old package files.
        /// </summary>
        /// <param name="packagePath">The path to the package file to delete.</param>
        public void CleanupPackage(string packagePath)
        {
            try
            {
                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                    _logger.LogInformation("Cleaned up package file: {PackagePath}", packagePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup package file: {PackagePath}", packagePath);
            }
        }
    }
}
