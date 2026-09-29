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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for executing C# and Python code from extracted NuGet packages.
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
    /// Expects a BlazorDataOrchestratorJob class with ExecuteJob method 
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

            // A job's C# source is every .cs file under CodeCSharp; they compile together
            // into one assembly. main.cs is a convention, not a requirement.
            var sourceFiles = Directory
                .GetFiles(codeFolder, "*.cs", SearchOption.AllDirectories)
                .OrderBy(p => !Path.GetFileName(p).Equals("main.cs", StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => Path.GetRelativePath(codeFolder, p), StringComparer.Ordinal)
                .ToList();

            if (sourceFiles.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No C# files found in CodeCSharp folder.";
                return result;
            }

            result.Logs.Add($"Compiling {sourceFiles.Count} C# file(s): " +
                string.Join(", ", sourceFiles.Select(f => Path.GetRelativePath(codeFolder, f))));

            var sources = new List<(string Path, string Text)>(sourceFiles.Count);
            foreach (var file in sourceFiles)
            {
                sources.Add((file, await File.ReadAllTextAsync(file)));
            }

            // Parse NuGet requirements from comments across every source file, so a helper
            // file's headers are not lost. Informational only — resolution uses the nuspec.
            var nugetPackages = sources
                .SelectMany(s => ParseNuGetRequirements(s.Text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
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
                            ReferenceNuGetAssembly(evaluator, assemblyPath, resolvedAssemblyPaths, result.Logs);
                        }
                    }
                    else if (!resolution.Success)
                    {
                        result.Logs.Add($"Warning: NuGet resolution failed: {resolution.ErrorMessage}");
                    }
                }
            }

            // Security: Reject packages that contain .dll files.
            // Only NuGet package dependencies (resolved via .nuspec) are allowed.
            var dlls = Directory.GetFiles(extractedPath, "*.dll", SearchOption.AllDirectories);
            if (dlls.Length > 0)
            {
                var dllNames = string.Join(", ", dlls.Select(Path.GetFileName));
                result.Success = false;
                result.ErrorMessage = $"Package contains .dll files which are not allowed. Use NuGet packages instead. Found: {dllNames}";
                result.Logs.Add($"Security: Rejected package containing .dll files: {dllNames}");
                return result;
            }

            foreach (var common in JobCompilationReferences.Common)
            {
                evaluator.ReferenceAssembly(common);
            }

            result.Logs.Add("Compiling C# code...");

            Assembly assembly;

            if (AzureEnvironmentDetector.IsAzureContainerApp)
            {
                // Azure path — compile in-memory to avoid CS-Script assembly probing issues
                assembly = CompileWithRoslyn(sources, result.Logs, resolvedAssemblyPaths);
            }
            else if (sources.Count > 1)
            {
                // CS-Script's CompileCode takes a single script string, so multi-file jobs
                // go through Roslyn even locally. Single-file jobs keep the CS-Script path.
                assembly = CompileWithRoslyn(sources, result.Logs, resolvedAssemblyPaths);
            }
            else
            {
                // Local path — use existing CS-Script evaluator
                assembly = evaluator.CompileCode(sources[0].Text);
            }

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
                // Find the BlazorDataOrchestratorJob class. Roslyn only rejects a duplicate
                // declaration when both share a namespace, so check for ambiguity here.
                var candidates = assembly.GetTypes()
                    .Where(t => t.Name == "BlazorDataOrchestratorJob")
                    .ToList();

                if (candidates.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "Class 'BlazorDataOrchestratorJob' not found. Code must define a class named 'BlazorDataOrchestratorJob'.";
                    return result;
                }

                if (candidates.Count > 1)
                {
                    result.Success = false;
                    result.ErrorMessage = "Multiple 'BlazorDataOrchestratorJob' types were found: " +
                        string.Join(", ", candidates.Select(t => t.FullName)) +
                        ". Exactly one is required.";
                    return result;
                }

                var jobType = candidates[0];

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
    /// Adds one NuGet-resolved assembly to the compilation and to the runtime resolve list.
    /// </summary>
    private static void ReferenceNuGetAssembly(IEvaluator evaluator, string assemblyPath,
        List<string> resolvedAssemblyPaths, List<string> logs)
    {
        var fileName = Path.GetFileName(assemblyPath);
        var simpleName = Path.GetFileNameWithoutExtension(assemblyPath);

        try
        {
            // Referencing both the host copy and the NuGet copy causes CS0433/CS0121.
            if (HostAssemblyCatalog.IsHostProvided(simpleName))
            {
                // The host copy may not be loaded yet, and CS-Script only sees what is referenced or loaded.
                evaluator.ReferenceAssembly(Assembly.Load(new AssemblyName(simpleName)));
                logs.Add($"  ~ Skipping {fileName} (already provided by host)");
                return;
            }

            // Default-context loads cannot be undone, so an earlier job (even a failed one) may already hold it.
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                !a.IsDynamic &&
                System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a) == System.Runtime.Loader.AssemblyLoadContext.Default &&
                string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

            if (loaded == null)
            {
                evaluator.ReferenceAssembly(assemblyPath);
                resolvedAssemblyPaths.Add(assemblyPath);
                logs.Add($"  + {fileName}");
                return;
            }

            var location = string.IsNullOrEmpty(loaded.Location) ? assemblyPath : loaded.Location;
            var loadedVersion = loaded.GetName().Version;
            Version? requestedVersion = null;
            try { requestedVersion = AssemblyName.GetAssemblyName(assemblyPath).Version; } catch { }

            evaluator.ReferenceAssembly(location);
            resolvedAssemblyPaths.Add(location);

            if (string.Equals(location, assemblyPath, StringComparison.OrdinalIgnoreCase) ||
                requestedVersion == null || requestedVersion == loadedVersion)
            {
                logs.Add($"  = Reusing {fileName} (loaded by an earlier job)");
            }
            else
            {
                logs.Add($"  ! Version conflict for {simpleName}: this job requests {requestedVersion}, " +
                    $"but an earlier job loaded {loadedVersion} from '{location}'. Using {loadedVersion}. " +
                    $"Restart the agent to load {requestedVersion}.");
            }
        }
        catch (Exception ex)
        {
            logs.Add($"  ! Failed to load {fileName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Compiles C# code using Roslyn in-memory compilation.
    /// Used in Azure Container Apps to avoid CS-Script's file-system assembly probing
    /// issues (Bad IL format errors), and for any multi-file job, which CS-Script's
    /// single-string CompileCode cannot express.
    /// </summary>
    private Assembly CompileWithRoslyn(IReadOnlyList<(string Path, string Text)> sources, List<string> logs,
        List<string> resolvedAssemblyPaths)
    {
        logs.Add($"Using in-memory Roslyn compilation for {sources.Count} file(s).");

        // Passing the real path makes every diagnostic and stack frame name the correct file.
        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(SourceText.From(s.Text, Encoding.UTF8), path: s.Path))
            .ToArray();

        // 1. Gather MetadataReferences from currently loaded assemblies
        var references = new List<MetadataReference>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                continue;

            try { references.Add(MetadataReference.CreateFromFile(asm.Location)); }
            catch { /* skip unreadable assemblies */ }
        }

        // 2. Add Trusted Platform Assemblies (runtime facades)
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator) ?? Array.Empty<string>();

        var existingPaths = new HashSet<string>(
            references.Select(r => r.Display ?? ""), StringComparer.OrdinalIgnoreCase);

        foreach (var path in tpa)
        {
            if (!existingPaths.Contains(path) && File.Exists(path))
            {
                try { references.Add(MetadataReference.CreateFromFile(path)); existingPaths.Add(path); }
                catch { }
            }
        }

        foreach (var common in JobCompilationReferences.Common)
        {
            if (!string.IsNullOrEmpty(common.Location) && existingPaths.Add(common.Location))
            {
                references.Add(MetadataReference.CreateFromFile(common.Location));
            }
        }

        // 3. Add NuGet-resolved assemblies
        foreach (var asmPath in resolvedAssemblyPaths)
        {
            if (!existingPaths.Contains(asmPath) && File.Exists(asmPath))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(asmPath));
                    logs.Add($"  + Roslyn ref: {Path.GetFileName(asmPath)}");
                }
                catch { }
            }
        }

        // 4. Compile
        var compilation = CSharpCompilation.Create(
            assemblyName: $"Job_{Guid.NewGuid():N}",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            throw new InvalidOperationException(
                $"Roslyn compilation failed:\n{string.Join("\n", errors)}");
        }

        ms.Seek(0, SeekOrigin.Begin);

        // 5. Load into a collectible ALC to avoid leaking assemblies
        var alc = new System.Runtime.Loader.AssemblyLoadContext(
            $"JobALC_{Guid.NewGuid():N}", isCollectible: true);
        return alc.LoadFromStream(ms);
    }

    /// <summary>
    /// Executes Python code using subprocess.
    /// </summary>
    private async Task<CodeExecutionResult> ExecutePythonAsync(string extractedPath, JobExecutionContext context)
    {
        var result = new CodeExecutionResult();
        result.StartTime = DateTime.UtcNow;

        // Returned values travel through a file, not stdout, so printed-and-returned lines are not logged twice.
        var resultFilePath = Path.Combine(Path.GetTempPath(), $"bdo-py-result-{Guid.NewGuid():N}.json");

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
            var pythonPath = FindPythonExecutable(out var pythonFailure);
            if (pythonPath == null)
            {
                result.Success = false;
                result.ErrorMessage = pythonFailure == PythonLocatorFailure.WindowsStoreStubOnly
                    ? $"Python executable not found ({pythonFailure}). {PythonLocator.WindowsStoreStubGuidance}"
                    : $"Python executable not found ({pythonFailure}). Ensure Python 3 is installed and in PATH.";
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
            psi.Environment["BLAZOR_ORCHESTRATOR_WEB_API_PARAMETER"] = context.WebAPIParameter ?? string.Empty;
            psi.Environment["BLAZOR_ORCHESTRATOR_RESULT_FILE"] = resultFilePath;

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var stdoutLines = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (stdoutLines)
                    {
                        outputBuilder.AppendLine(e.Data);
                        stdoutLines.Add(e.Data);
                        result.Logs.Add(e.Data);
                    }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (errorBuilder)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
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

            // The timed overload does not wait for the async output handlers to drain.
            process.WaitForExit();

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
                MergeReturnedPythonLogs(resultFilePath, stdoutLines, result.Logs);
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
            try { File.Delete(resultFilePath); } catch { }
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
        var pythonPath = FindPythonExecutable(out _);
        if (pythonPath == null)
        {
            logs.Add("Warning: Python not found, skipping dependency installation.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            // Add --break-system-packages for Debian 12+ containers (PEP 668)
            Arguments = $"-m pip install -r \"{requirementsPath}\" --quiet --break-system-packages",
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

        return $@"
import sys
import os
import json
import inspect

# Add the code directory to the path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Import the main module
import {mainModule}

# Call execute_job with the context
app_settings = os.environ.get('BLAZOR_ORCHESTRATOR_APP_SETTINGS', '{{}}')
job_id = int(os.environ.get('BLAZOR_ORCHESTRATOR_JOB_ID', '0'))
job_instance_id = int(os.environ.get('BLAZOR_ORCHESTRATOR_JOB_INSTANCE_ID', '0'))
job_schedule_id = int(os.environ.get('BLAZOR_ORCHESTRATOR_JOB_SCHEDULE_ID', '0'))
web_api_parameter = os.environ.get('BLAZOR_ORCHESTRATOR_WEB_API_PARAMETER', '')
result_file = os.environ.get('BLAZOR_ORCHESTRATOR_RESULT_FILE', '')

kwargs = dict(
    app_settings=app_settings,
    job_agent_id=0,
    job_id=job_id,
    job_instance_id=job_instance_id,
    job_schedule_id=job_schedule_id
)

# Older jobs declare only five parameters; pass web_api_parameter only when accepted.
params = inspect.signature({mainModule}.execute_job).parameters
if 'web_api_parameter' in params or any(p.kind == p.VAR_KEYWORD for p in params.values()):
    kwargs['web_api_parameter'] = web_api_parameter

result = {mainModule}.execute_job(**kwargs)

# Returned values go to the result file; the host merges them with the captured stdout.
if result_file:
    with open(result_file, 'w', encoding='utf-8') as f:
        json.dump(result if result is not None else [], f, default=str)
";
    }

    /// <summary>
    /// Appends the job's returned log items that were not already printed to stdout.
    /// </summary>
    internal static void MergeReturnedPythonLogs(string resultFilePath, IReadOnlyList<string> stdoutLines, List<string> logs)
    {
        if (!File.Exists(resultFilePath))
        {
            logs.Add("Warning: Python job did not produce a result file; returned values were not captured.");
            return;
        }

        System.Text.Json.JsonElement root;
        try
        {
            root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(resultFilePath)).RootElement.Clone();
        }
        catch (System.Text.Json.JsonException ex)
        {
            logs.Add($"Warning: Python job result is not valid JSON and was ignored: {ex.Message}");
            return;
        }

        if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            logs.Add($"Warning: execute_job returned {root.ValueKind} instead of a list; the value was ignored.");
            return;
        }

        foreach (var item in root.EnumerateArray())
        {
            var text = item.ValueKind == System.Text.Json.JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            // The suffix check covers the template's "[timestamp] [level] message" print format.
            if (!stdoutLines.Any(line => line == text || line.EndsWith(text, StringComparison.Ordinal)))
            {
                logs.Add(text);
            }
        }
    }

    /// <summary>
    /// Finds a real Python 3 executable on the system.
    /// </summary>
    private static string? FindPythonExecutable(out PythonLocatorFailure reason)
    {
        return PythonLocator.TryLocate(out var path, out reason) ? path : null;
    }

    /// <summary>
    /// Cleans up state between job executions to prevent memory leaks and state bleed.
    /// Resets the CSScript evaluator and forces unloading of collectible AssemblyLoadContexts.
    /// Must be called after every job execution, regardless of success or failure.
    /// </summary>
    public void CleanupAfterExecution()
    {
        try
        {
            // Reset the CSScript evaluator to clear all cached references and compiled assemblies
            CSScript.Evaluator.Reset();
        }
        catch
        {
            // Evaluator may not have been initialized — safe to ignore
        }
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
