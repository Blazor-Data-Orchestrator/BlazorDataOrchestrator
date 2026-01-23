using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlazorDataOrchestrator.Core.Models;
using CSScriptLib;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for executing C# and Python code from extracted NuGet packages.
/// Follows the patterns defined in csharp.instructions.md and python.instructions.md.
/// </summary>
public class CodeExecutorService
{
    private readonly PackageProcessorService _packageProcessor;
    private readonly NuGetResolverService _nugetResolver;

    public CodeExecutorService(PackageProcessorService packageProcessor)
    {
        _packageProcessor = packageProcessor;
        _nugetResolver = new NuGetResolverService();
    }

    /// <summary>
    /// Executes code from an extracted package based on the configuration.
    /// </summary>
    /// <param name="extractedPath">The path where the package was extracted</param>
    /// <param name="context">The execution context containing job information</param>
    /// <returns>Execution result with logs and status</returns>
    public async Task<CodeExecutionResult> ExecuteAsync(string extractedPath, JobExecutionContext context)
    {
        var result = new CodeExecutionResult();
        result.StartTime = DateTime.UtcNow;

        try
        {
            // Get configuration
            var config = await _packageProcessor.GetConfigurationAsync(extractedPath);
            var language = context.SelectedLanguage ?? config.SelectedLanguage ?? "CSharp";

            result.Logs.Add($"Executing job with language: {language}");

            if (language.Equals("Python", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecutePythonAsync(extractedPath, context);
            }
            else
            {
                return await ExecuteCSharpAsync(extractedPath, context);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.StackTrace = ex.StackTrace;
            result.Logs.Add($"Execution failed: {ex.Message}");
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// Executes C# code using CSScript.
    /// Expects a BlazorDataOrchestratorJob class with ExecuteJob method per csharp.instructions.md.
    /// </summary>
    private async Task<CodeExecutionResult> ExecuteCSharpAsync(string extractedPath, JobExecutionContext context)
    {
        var result = new CodeExecutionResult();
        result.StartTime = DateTime.UtcNow;

        try
        {
            // Find the CodeCSharp folder
            var codeFolder = _packageProcessor.GetCodeFolderPath(extractedPath, "CSharp");
            if (codeFolder == null)
            {
                result.Success = false;
                result.ErrorMessage = "CodeCSharp folder not found in package.";
                return result;
            }

            // Find main.cs
            var mainCsPath = Path.Combine(codeFolder, "main.cs");
            if (!File.Exists(mainCsPath))
            {
                // Try to find any .cs file
                var csFiles = Directory.GetFiles(codeFolder, "*.cs", SearchOption.AllDirectories);
                if (csFiles.Length == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "No C# files found in CodeCSharp folder.";
                    return result;
                }
                mainCsPath = csFiles[0];
            }

            result.Logs.Add($"Loading C# code from: {Path.GetFileName(mainCsPath)}");

            // Read the code
            var code = await File.ReadAllTextAsync(mainCsPath);

            // Parse NuGet requirements from comments
            var nugetPackages = ParseNuGetRequirements(code);
            if (nugetPackages.Any())
            {
                result.Logs.Add($"Required NuGet packages: {string.Join(", ", nugetPackages)}");
            }

            // Configure CSScript evaluator
            var evaluator = CSScript.Evaluator;
            evaluator.Reset();

            // Track resolved assemblies for runtime loading
            var resolvedAssemblyPaths = new List<string>();

            // Resolve NuGet dependencies from .nuspec
            var dependencyGroups = await _packageProcessor.GetDependenciesFromNuSpecAsync(extractedPath);
            if (dependencyGroups.Any())
            {
                // Determine target framework (default to net10.0)
                var targetFramework = "net10.0";
                var bestGroup = _packageProcessor.GetBestMatchingDependencyGroup(dependencyGroups, targetFramework);
                
                if (bestGroup != null && bestGroup.Dependencies.Count > 0)
                {
                    result.Logs.Add($"Found {bestGroup.Dependencies.Count} NuGet dependencies for {bestGroup.TargetFramework ?? "any"} framework:");
                    foreach (var dep in bestGroup.Dependencies)
                    {
                        result.Logs.Add($"  - {dep.PackageId} {dep.Version}");
                    }

                    // Use the target framework from the dependency group if available
                    if (!string.IsNullOrEmpty(bestGroup.TargetFramework))
                    {
                        targetFramework = bestGroup.TargetFramework;
                    }

                    // Resolve and download dependencies
                    var resolution = await _nugetResolver.ResolveAsync(
                        bestGroup.Dependencies, 
                        targetFramework, 
                        result.Logs);

                    if (resolution.Success && resolution.AssemblyPaths.Count > 0)
                    {
                        result.Logs.Add($"Resolved {resolution.AssemblyPaths.Count} assemblies from NuGet packages:");
                        foreach (var assemblyPath in resolution.AssemblyPaths)
                        {
                            try
                            {
                                evaluator.ReferenceAssembly(assemblyPath);
                                resolvedAssemblyPaths.Add(assemblyPath);
                                result.Logs.Add($"  + {Path.GetFileName(assemblyPath)}");
                            }
                            catch (Exception ex)
                            {
                                result.Logs.Add($"  ! Failed to load {Path.GetFileName(assemblyPath)}: {ex.Message}");
                            }
                        }
                    }
                    else if (!resolution.Success)
                    {
                        result.Logs.Add($"Warning: NuGet resolution failed: {resolution.ErrorMessage}");
                    }
                }
            }

            // Add references to DLLs found in the package
            var dlls = Directory.GetFiles(extractedPath, "*.dll", SearchOption.AllDirectories);
            foreach (var dll in dlls)
            {
                try
                {
                    evaluator.ReferenceAssembly(dll);
                    result.Logs.Add($"Added reference: {Path.GetFileName(dll)}");
                }
                catch
                {
                    // Ignore DLLs that can't be loaded
                }
            }

            // Add common references
            evaluator.ReferenceAssembly(typeof(System.Text.Json.JsonSerializer).Assembly);
            evaluator.ReferenceAssemblyOf<Microsoft.EntityFrameworkCore.DbContext>();

            // Add reference to BlazorDataOrchestrator.Core
            evaluator.ReferenceAssemblyOf<JobManager>();

            result.Logs.Add("Compiling C# code...");

            // Compile and load the assembly
            var assembly = evaluator.CompileCode(code);

            // Pre-load all resolved assemblies into the current AppDomain
            // This is required so that GetTypes() can resolve dependencies
            var loadedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var assemblyPath in resolvedAssemblyPaths)
            {
                try
                {
                    var loadedAsm = Assembly.LoadFrom(assemblyPath);
                    var asmName = loadedAsm.GetName().Name;
                    if (asmName != null && !loadedAssemblies.ContainsKey(asmName))
                    {
                        loadedAssemblies[asmName] = loadedAsm;
                    }
                }
                catch (Exception ex)
                {
                    result.Logs.Add($"Warning: Could not pre-load {Path.GetFileName(assemblyPath)}: {ex.Message}");
                }
            }

            // Also load DLLs from the package
            foreach (var dll in dlls)
            {
                try
                {
                    var loadedAsm = Assembly.LoadFrom(dll);
                    var asmName = loadedAsm.GetName().Name;
                    if (asmName != null && !loadedAssemblies.ContainsKey(asmName))
                    {
                        loadedAssemblies[asmName] = loadedAsm;
                    }
                }
                catch
                {
                    // Ignore DLLs that can't be loaded
                }
            }

            result.Logs.Add($"Pre-loaded {loadedAssemblies.Count} assemblies for runtime resolution.");

            // Set up assembly resolve handler for the compiled script
            ResolveEventHandler? resolveHandler = null;
            resolveHandler = (sender, args) =>
            {
                var requestedName = new AssemblyName(args.Name).Name;
                if (requestedName != null && loadedAssemblies.TryGetValue(requestedName, out var asm))
                {
                    return asm;
                }
                return null;
            };

            AppDomain.CurrentDomain.AssemblyResolve += resolveHandler;

            try
            {
                // Find the BlazorDataOrchestratorJob class
                var jobType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "BlazorDataOrchestratorJob");

                if (jobType == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Class 'BlazorDataOrchestratorJob' not found. Code must define a class named 'BlazorDataOrchestratorJob'.";
                    return result;
                }

                // Find the ExecuteJob method - try 6-parameter version first (with webAPIParameter)
                var executeMethod = jobType.GetMethod("ExecuteJob",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(string) },
                    null);

                bool has6Params = executeMethod != null;

                // Fall back to 5-parameter version (without webAPIParameter)
                if (executeMethod == null)
                {
                    executeMethod = jobType.GetMethod("ExecuteJob",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(int) },
                        null);
                }

                if (executeMethod == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Method 'ExecuteJob' not found in BlazorDataOrchestratorJob class. " +
                        "Expected signature: public static async Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId[, string webAPIParameter])";
                    return result;
                }

                result.Logs.Add("Executing job...");

                // Execute the method with appropriate parameters
                object[] methodParams;
                if (has6Params)
                {
                    methodParams = new object[]
                    {
                        context.AppSettingsJson,
                        0, // jobAgentId
                        context.JobId,
                        context.JobInstanceId,
                        context.JobScheduleId,
                        context.WebAPIParameter ?? string.Empty
                    };
                }
                else
                {
                    methodParams = new object[]
                    {
                        context.AppSettingsJson,
                        0, // jobAgentId
                        context.JobId,
                        context.JobInstanceId,
                        context.JobScheduleId
                    };
                }

                var task = (Task<List<string>>?)executeMethod.Invoke(null, methodParams);

                if (task != null)
                {
                    var logs = await task;
                    result.Logs.AddRange(logs);
                }

                result.Success = true;
                result.Logs.Add("C# job execution completed successfully.");
            }
            finally
            {
                // Always remove the assembly resolve handler
                AppDomain.CurrentDomain.AssemblyResolve -= resolveHandler;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.StackTrace = ex.StackTrace;
            result.Logs.Add($"C# execution error: {ex.Message}");

            // Include inner exception details
            if (ex.InnerException != null)
            {
                result.Logs.Add($"Inner exception: {ex.InnerException.Message}");
            }
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// Executes Python code using subprocess.
    /// Expects an execute_job function per python.instructions.md.
    /// </summary>
    private async Task<CodeExecutionResult> ExecutePythonAsync(string extractedPath, JobExecutionContext context)
    {
        var result = new CodeExecutionResult();
        result.StartTime = DateTime.UtcNow;

        try
        {
            // Find the CodePython folder
            var codeFolder = _packageProcessor.GetCodeFolderPath(extractedPath, "Python");
            if (codeFolder == null)
            {
                result.Success = false;
                result.ErrorMessage = "CodePython folder not found in package.";
                return result;
            }

            // Find main.py
            var mainPyPath = Path.Combine(codeFolder, "main.py");
            if (!File.Exists(mainPyPath))
            {
                // Try to find any .py file
                var pyFiles = Directory.GetFiles(codeFolder, "*.py", SearchOption.AllDirectories);
                if (pyFiles.Length == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "No Python files found in CodePython folder.";
                    return result;
                }
                mainPyPath = pyFiles[0];
            }

            result.Logs.Add($"Loading Python code from: {Path.GetFileName(mainPyPath)}");

            // Check for requirements.txt and install dependencies
            var requirementsPath = Path.Combine(codeFolder, "requirements.txt");
            if (File.Exists(requirementsPath))
            {
                result.Logs.Add("Installing Python dependencies...");
                await InstallPythonDependenciesAsync(requirementsPath, result.Logs);
            }

            // Create a runner script that imports main.py and calls execute_job
            var runnerScript = CreatePythonRunnerScript(mainPyPath, context);
            var runnerPath = Path.Combine(codeFolder, "_runner.py");
            await File.WriteAllTextAsync(runnerPath, runnerScript);

            result.Logs.Add("Executing Python code...");

            // Execute Python
            var pythonPath = FindPythonExecutable();
            if (pythonPath == null)
            {
                result.Success = false;
                result.ErrorMessage = "Python executable not found. Ensure Python is installed and in PATH.";
                return result;
            }

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{runnerPath}\"",
                WorkingDirectory = codeFolder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Add environment variables
            psi.Environment["BLAZOR_ORCHESTRATOR_APP_SETTINGS"] = context.AppSettingsJson;
            psi.Environment["BLAZOR_ORCHESTRATOR_JOB_ID"] = context.JobId.ToString();
            psi.Environment["BLAZOR_ORCHESTRATOR_JOB_INSTANCE_ID"] = context.JobInstanceId.ToString();
            psi.Environment["BLAZOR_ORCHESTRATOR_JOB_SCHEDULE_ID"] = context.JobScheduleId.ToString();

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    result.Logs.Add(e.Data);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for completion with timeout (5 minutes)
            var completed = await Task.Run(() => process.WaitForExit(300000));

            if (!completed)
            {
                process.Kill();
                result.Success = false;
                result.ErrorMessage = "Python execution timed out after 5 minutes.";
                return result;
            }

            // Clean up runner script
            try { File.Delete(runnerPath); } catch { }

            if (process.ExitCode != 0)
            {
                result.Success = false;
                result.ErrorMessage = $"Python execution failed with exit code {process.ExitCode}";
                if (errorBuilder.Length > 0)
                {
                    result.ErrorMessage += $": {errorBuilder}";
                    result.Logs.Add($"Python error: {errorBuilder}");
                }
            }
            else
            {
                result.Success = true;
                result.Logs.Add("Python job execution completed successfully.");
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.StackTrace = ex.StackTrace;
            result.Logs.Add($"Python execution error: {ex.Message}");
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// Parses NuGet requirements from code comments.
    /// Format: // NUGET: PackageId, Version
    /// </summary>
    private List<string> ParseNuGetRequirements(string code)
    {
        var packages = new List<string>();
        var regex = new Regex(@"//\s*(?:NUGET|REQUIRES\s+NUGET):\s*(.+)", RegexOptions.IgnoreCase);

        foreach (Match match in regex.Matches(code))
        {
            packages.Add(match.Groups[1].Value.Trim());
        }

        return packages;
    }

    /// <summary>
    /// Installs Python dependencies from requirements.txt
    /// </summary>
    private async Task InstallPythonDependenciesAsync(string requirementsPath, List<string> logs)
    {
        var pythonPath = FindPythonExecutable();
        if (pythonPath == null)
        {
            logs.Add("Warning: Python not found, skipping dependency installation.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"-m pip install -r \"{requirementsPath}\" --quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await Task.Run(() => process.WaitForExit(120000)); // 2 minute timeout

        if (!string.IsNullOrWhiteSpace(output))
            logs.Add($"pip: {output}");
        if (!string.IsNullOrWhiteSpace(error) && process.ExitCode != 0)
            logs.Add($"pip error: {error}");
    }

    /// <summary>
    /// Creates a Python runner script that imports main.py and calls execute_job.
    /// </summary>
    private string CreatePythonRunnerScript(string mainPyPath, JobExecutionContext context)
    {
        var mainModule = Path.GetFileNameWithoutExtension(mainPyPath);
        var appSettingsEscaped = context.AppSettingsJson
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        return $@"
import sys
import os
import json

# Add the code directory to the path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Import the main module
import {mainModule}

# Call execute_job with the context
app_settings = os.environ.get('BLAZOR_ORCHESTRATOR_APP_SETTINGS', '{{}}')
job_id = int(os.environ.get('BLAZOR_ORCHESTRATOR_JOB_ID', '0'))
job_instance_id = int(os.environ.get('BLAZOR_ORCHESTRATOR_JOB_INSTANCE_ID', '0'))
job_schedule_id = int(os.environ.get('BLAZOR_ORCHESTRATOR_JOB_SCHEDULE_ID', '0'))

# Call the execute_job function
result = {mainModule}.execute_job(
    app_settings=app_settings,
    job_agent_id=0,
    job_id=job_id,
    job_instance_id=job_instance_id,
    job_schedule_id=job_schedule_id
)

# Print results
if result:
    for log in result:
        print(log)
";
    }

    /// <summary>
    /// Finds the Python executable on the system.
    /// </summary>
    private string? FindPythonExecutable()
    {
        // Check common locations
        var candidates = new[]
        {
            "python",
            "python3",
            "py",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
            @"C:\Python39\python.exe",
            @"C:\Program Files\Python311\python.exe",
            @"C:\Program Files\Python310\python.exe",
            "/usr/bin/python3",
            "/usr/local/bin/python3"
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();
                process.WaitForExit(5000);

                if (process.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // Try next candidate
            }
        }

        return null;
    }
}

/// <summary>
/// Result of code execution.
/// </summary>
public class CodeExecutionResult
{
    /// <summary>
    /// Whether the execution was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Stack trace if an exception occurred.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Log messages from the execution.
    /// </summary>
    public List<string> Logs { get; set; } = new();

    /// <summary>
    /// When execution started.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// When execution ended.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Total execution duration.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;
}
