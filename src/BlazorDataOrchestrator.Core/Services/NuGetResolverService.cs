using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for resolving NuGet package dependencies and downloading their assemblies.
/// Uses `dotnet restore` to properly resolve transitive dependencies.
/// </summary>
public class NuGetResolverService
{
    private readonly string _globalPackagesPath;
    private readonly string _tempProjectPath;

    public NuGetResolverService()
    {
        // Use standard NuGet global packages folder
        _globalPackagesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        // Temp folder for generated projects
        _tempProjectPath = Path.Combine(Path.GetTempPath(), "BlazorDataOrchestrator", "NuGetResolver");
    }

    /// <summary>
    /// Resolves NuGet dependencies and returns paths to all required assemblies.
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
            // Create a unique temp folder for this resolution
            var projectId = Guid.NewGuid().ToString("N")[..8];
            var projectFolder = Path.Combine(_tempProjectPath, projectId);
            Directory.CreateDirectory(projectFolder);

            try
            {
                // Generate the temporary project file
                var csprojPath = Path.Combine(projectFolder, "TempResolve.csproj");
                await GenerateProjectFileAsync(csprojPath, dependencies, targetFramework);
                logs.Add($"Generated temporary project for dependency resolution.");

                // Run dotnet restore
                var restoreResult = await RunDotNetRestoreAsync(projectFolder, logs);
                if (!restoreResult)
                {
                    result.Success = false;
                    result.ErrorMessage = "dotnet restore failed. Check logs for details.";
                    return result;
                }

                // Parse the project.assets.json to get resolved assemblies
                var assetsPath = Path.Combine(projectFolder, "obj", "project.assets.json");
                if (!File.Exists(assetsPath))
                {
                    result.Success = false;
                    result.ErrorMessage = "project.assets.json not found after restore.";
                    return result;
                }

                var assemblies = await ParseAssetsFileAsync(assetsPath, targetFramework, logs);
                result.AssemblyPaths = assemblies;
                result.Success = true;

                logs.Add($"Resolved {assemblies.Count} assemblies from NuGet dependencies.");
            }
            finally
            {
                // Clean up temp folder
                try
                {
                    if (Directory.Exists(projectFolder))
                    {
                        Directory.Delete(projectFolder, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
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
    /// Generates a temporary .csproj file with the specified dependencies.
    /// </summary>
    private async Task GenerateProjectFileAsync(
        string csprojPath,
        List<NuGetDependency> dependencies,
        string targetFramework)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
        sb.AppendLine("    <OutputType>Library</OutputType>");
        sb.AppendLine("    <EnableDefaultItems>false</EnableDefaultItems>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");

        foreach (var dep in dependencies)
        {
            // Clean the version string (remove brackets for version ranges)
            var version = dep.Version;
            if (version.StartsWith("[") || version.StartsWith("("))
            {
                // For version ranges, extract the minimum version
                version = ExtractMinVersion(version);
            }

            sb.AppendLine($"    <PackageReference Include=\"{dep.PackageId}\" Version=\"{version}\" />");
        }

        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</Project>");

        await File.WriteAllTextAsync(csprojPath, sb.ToString());
    }

    /// <summary>
    /// Extracts the minimum version from a NuGet version range.
    /// </summary>
    private string ExtractMinVersion(string versionRange)
    {
        // Handle common version range formats:
        // [1.0.0, 2.0.0) -> 1.0.0
        // [1.0.0] -> 1.0.0
        // (1.0.0, ) -> 1.0.0
        var trimmed = versionRange.TrimStart('[', '(').TrimEnd(']', ')');
        var parts = trimmed.Split(',');
        return parts[0].Trim();
    }

    /// <summary>
    /// Runs `dotnet restore` on the temporary project.
    /// </summary>
    private async Task<bool> RunDotNetRestoreAsync(string projectFolder, List<string> logs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "restore --verbosity minimal",
            WorkingDirectory = projectFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait with timeout (2 minutes)
        var completed = await Task.Run(() => process.WaitForExit(120000));

        if (!completed)
        {
            try { process.Kill(); } catch { }
            logs.Add("dotnet restore timed out after 2 minutes.");
            return false;
        }

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        if (!string.IsNullOrWhiteSpace(output))
        {
            logs.Add($"dotnet restore output: {output.Trim()}");
        }

        if (process.ExitCode != 0)
        {
            logs.Add($"dotnet restore failed with exit code {process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(error))
            {
                logs.Add($"Error: {error.Trim()}");
            }
            return false;
        }

        logs.Add("dotnet restore completed successfully.");
        return true;
    }

    /// <summary>
    /// Parses project.assets.json to extract all resolved assembly paths.
    /// </summary>
    private async Task<List<string>> ParseAssetsFileAsync(
        string assetsPath,
        string targetFramework,
        List<string> logs)
    {
        var assemblies = new List<string>();

        try
        {
            var json = await File.ReadAllTextAsync(assetsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Get the targets section
            if (!root.TryGetProperty("targets", out var targets))
            {
                logs.Add("Warning: No 'targets' section in project.assets.json");
                return assemblies;
            }

            // Find the matching target framework
            JsonElement targetElement = default;
            bool foundTarget = false;

            foreach (var target in targets.EnumerateObject())
            {
                // Target names are like "net10.0" or "net10.0/win-x64"
                if (target.Name.StartsWith(targetFramework, StringComparison.OrdinalIgnoreCase))
                {
                    targetElement = target.Value;
                    foundTarget = true;
                    break;
                }
            }

            if (!foundTarget)
            {
                logs.Add($"Warning: Target framework '{targetFramework}' not found in assets file.");
                // Try to use the first available target
                foreach (var target in targets.EnumerateObject())
                {
                    targetElement = target.Value;
                    logs.Add($"Using alternative target: {target.Name}");
                    foundTarget = true;
                    break;
                }
            }

            if (!foundTarget)
            {
                return assemblies;
            }

            // Get the packageFolders to resolve paths
            var packageFolders = new List<string>();
            if (root.TryGetProperty("packageFolders", out var folders))
            {
                foreach (var folder in folders.EnumerateObject())
                {
                    packageFolders.Add(folder.Name);
                }
            }

            if (packageFolders.Count == 0)
            {
                packageFolders.Add(_globalPackagesPath);
            }

            // Iterate through all packages in the target
            foreach (var package in targetElement.EnumerateObject())
            {
                // Package names are like "SendGrid/9.29.3"
                var packageParts = package.Name.Split('/');
                if (packageParts.Length != 2)
                    continue;

                var packageId = packageParts[0];
                var version = packageParts[1];

                // Get runtime assemblies
                if (package.Value.TryGetProperty("runtime", out var runtime))
                {
                    foreach (var assembly in runtime.EnumerateObject())
                    {
                        // assembly.Name is like "lib/net6.0/SendGrid.dll"
                        var assemblyRelPath = assembly.Name;

                        // Skip resource assemblies and other non-dll files
                        if (!assemblyRelPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Skip resource/locale assemblies
                        if (assemblyRelPath.Contains("/resources/") || 
                            assemblyRelPath.Contains("\\resources\\"))
                            continue;

                        // Build full path
                        foreach (var packageFolder in packageFolders)
                        {
                            var fullPath = Path.Combine(
                                packageFolder,
                                packageId.ToLowerInvariant(),
                                version,
                                assemblyRelPath.Replace('/', Path.DirectorySeparatorChar));

                            if (File.Exists(fullPath))
                            {
                                if (!assemblies.Contains(fullPath))
                                {
                                    assemblies.Add(fullPath);
                                }
                                break;
                            }
                        }
                    }
                }

                // Also check compile assemblies if runtime is empty
                if (package.Value.TryGetProperty("compile", out var compile))
                {
                    foreach (var assembly in compile.EnumerateObject())
                    {
                        var assemblyRelPath = assembly.Name;

                        if (!assemblyRelPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Skip placeholder files
                        if (assemblyRelPath.Contains("_._"))
                            continue;

                        foreach (var packageFolder in packageFolders)
                        {
                            var fullPath = Path.Combine(
                                packageFolder,
                                packageId.ToLowerInvariant(),
                                version,
                                assemblyRelPath.Replace('/', Path.DirectorySeparatorChar));

                            if (File.Exists(fullPath))
                            {
                                if (!assemblies.Contains(fullPath))
                                {
                                    assemblies.Add(fullPath);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            logs.Add($"Found {assemblies.Count} assemblies from package resolution.");
        }
        catch (Exception ex)
        {
            logs.Add($"Error parsing assets file: {ex.Message}");
        }

        return assemblies;
    }
}
