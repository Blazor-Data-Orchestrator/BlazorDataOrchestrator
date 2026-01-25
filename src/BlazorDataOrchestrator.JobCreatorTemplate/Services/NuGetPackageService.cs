using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services
{
    /// <summary>
    /// Service for creating NuGet packages from the job code.
    /// This is a wrapper around the Core NuGetPackageBuilderService that adds
    /// environment-specific functionality for the JobCreatorTemplate.
    /// </summary>
    public class NuGetPackageService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<NuGetPackageService> _logger;
        private readonly NuGetPackageBuilderService _builderService;

        public NuGetPackageService(IWebHostEnvironment environment, ILogger<NuGetPackageService> logger)
        {
            _environment = environment;
            _logger = logger;
            _builderService = new NuGetPackageBuilderService();
        }

        /// <summary>
        /// Extracts all NuGet package references from the project file and updates dependencies.json.
        /// </summary>
        /// <returns>The list of extracted dependencies.</returns>
        public async Task<List<PackageDependency>> ExtractAndSaveDependenciesFromProjectAsync()
        {
            var projectFile = Path.Combine(_environment.ContentRootPath, "BlazorDataOrchestrator.JobCreatorTemplate.csproj");

            if (!File.Exists(projectFile))
            {
                _logger.LogWarning("Project file not found: {ProjectFile}", projectFile);
                return new List<PackageDependency>();
            }

            try
            {
                // Use the Core service to extract dependencies
                var dependencies = await _builderService.ExtractDependenciesFromProjectAsync(projectFile);

                // Add essential dependencies
                foreach (var defaultDep in NuGetPackageBuilderService.DefaultDependencies)
                {
                    if (!dependencies.Any(d => d.Id.Equals(defaultDep.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        dependencies.Add(defaultDep);
                    }
                }

                // Save to dependencies.json
                var csharpFolder = Path.Combine(_environment.ContentRootPath, "Code", "CodeCSharp");
                var dependenciesFile = Path.Combine(csharpFolder, "dependencies.json");
                
                await _builderService.SaveDependenciesAsync(dependenciesFile, dependencies);

                _logger.LogInformation("Saved {Count} dependencies to dependencies.json", dependencies.Count);

                foreach (var dep in dependencies)
                {
                    _logger.LogInformation("Extracted package dependency: {Id} v{Version}", dep.Id, dep.Version);
                }

                return dependencies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract dependencies from project file");
                return new List<PackageDependency>();
            }
        }

        /// <summary>
        /// Creates a NuGet package from the code files.
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
            var config = CreateBuildConfiguration(packageId, version, description, authors);
            
            var result = await _builderService.BuildPackageAsync(config);

            if (!result.Success)
            {
                _logger.LogError("Failed to create package: {Error}", result.ErrorMessage);
                throw new InvalidOperationException($"Failed to create package: {result.ErrorMessage}");
            }

            foreach (var log in result.Logs)
            {
                _logger.LogInformation("{Log}", log);
            }

            _logger.LogInformation("Created NuGet package: {PackagePath}", result.PackagePath);

            return result.PackagePath!;
        }

        /// <summary>
        /// Creates a NuGet package and returns it as a MemoryStream along with metadata.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package version (auto-generated if not provided).</param>
        /// <param name="description">The package description.</param>
        /// <param name="authors">The package authors.</param>
        /// <returns>Tuple containing the package stream, full filename, and version.</returns>
        public async Task<(MemoryStream PackageStream, string FileName, string Version)> CreatePackageAsStreamAsync(
            string packageId = "BlazorDataOrchestrator.Job",
            string? version = null,
            string? description = null,
            string? authors = null)
        {
            var config = CreateBuildConfiguration(packageId, version, description, authors);
            
            var result = await _builderService.BuildPackageAsStreamAsync(config);

            if (result == null)
            {
                _logger.LogError("Failed to create package as stream");
                throw new InvalidOperationException("Failed to create package as stream");
            }

            _logger.LogInformation("Created package as stream: {FileName} (version {Version})", result.Value.FileName, result.Value.Version);

            return result.Value;
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
                _builderService.CleanupPackage(packagePath);
                _logger.LogInformation("Cleaned up package file: {PackagePath}", packagePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup package file: {PackagePath}", packagePath);
            }
        }

        /// <summary>
        /// Creates the build configuration for the Core service.
        /// </summary>
        private NuGetPackageBuilderService.PackageBuildConfiguration CreateBuildConfiguration(
            string packageId,
            string? version,
            string? description,
            string? authors)
        {
            var baseCodeFolder = Path.Combine(_environment.ContentRootPath, "Code");
            var csharpFolder = Path.Combine(baseCodeFolder, "CodeCSharp");

            return new NuGetPackageBuilderService.PackageBuildConfiguration
            {
                CodeRootPath = baseCodeFolder,
                PackageId = packageId,
                Version = version,
                Description = description,
                Authors = authors,
                AppSettingsPath = Path.Combine(_environment.ContentRootPath, "appsettings.json"),
                AppSettingsProductionPath = Path.Combine(_environment.ContentRootPath, "appsettingsProduction.json"),
                DependenciesFilePath = Path.Combine(csharpFolder, "dependencies.json")
            };
        }
    }
}
