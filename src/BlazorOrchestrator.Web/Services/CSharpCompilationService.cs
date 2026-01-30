using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for compiling and validating C# code.
/// </summary>
public class CSharpCompilationService
{
    private readonly ILogger<CSharpCompilationService> _logger;
    private readonly NuGetResolverService _nugetResolver;

    public CSharpCompilationService(ILogger<CSharpCompilationService> logger)
    {
        _logger = logger;
        _nugetResolver = new NuGetResolverService();
    }

    /// <summary>
    /// Compiles C# code and returns compilation results.
    /// </summary>
    /// <param name="code">The C# code to compile.</param>
    /// <param name="assemblyName">The name for the compiled assembly.</param>
    /// <returns>Compilation result with success status and any errors.</returns>
    public CompilationResult Compile(string code, string assemblyName = "JobAssembly")
    {
        var result = new CompilationResult();

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);

            // Get references from current runtime
            var references = GetMetadataReferences();

            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            result.Success = emitResult.Success;

            if (!emitResult.Success)
            {
                result.Errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error ||
                                d.Severity == DiagnosticSeverity.Warning)
                    .Select(d => new CompilationError
                    {
                        Message = d.GetMessage(),
                        Line = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                        Column = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                        Severity = d.Severity == DiagnosticSeverity.Error ? "Error" : "Warning"
                    })
                    .ToList();
            }

            _logger.LogInformation("Compilation {Status} with {ErrorCount} issues",
                result.Success ? "succeeded" : "failed",
                result.Errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compilation failed with exception");
            result.Success = false;
            result.Errors.Add(new CompilationError
            {
                Message = $"Compilation exception: {ex.Message}",
                Line = 1,
                Column = 1,
                Severity = "Error"
            });
        }

        return result;
    }

    /// <summary>
    /// Compiles C# code with NuGet dependencies and standard references.
    /// Uses `dotnet restore` to properly resolve transitive dependencies.
    /// </summary>
    /// <param name="code">The C# code to compile.</param>
    /// <param name="dependencies">NuGet dependencies from .nuspec file.</param>
    /// <param name="assemblyName">The name for the compiled assembly.</param>
    /// <returns>Compilation result with success status and any errors.</returns>
    public async Task<CompilationResult> CompileWithDependenciesAsync(
        string code,
        List<NuGetDependencyInfo>? dependencies = null,
        string assemblyName = "JobAssembly")
    {
        var result = new CompilationResult();

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);

            // Get base references from current runtime
            var references = GetMetadataReferences().ToList();
            _logger.LogInformation("Base references count: {Count}", references.Count);

            // Add standard references commonly needed for jobs
            AddStandardReferences(references);
            _logger.LogInformation("After standard references: {Count}", references.Count);

            // Resolve NuGet dependencies using dotnet restore (like the Agent does)
            if (dependencies?.Any() == true)
            {
                _logger.LogInformation("Resolving {Count} NuGet dependencies via dotnet restore:", dependencies.Count);
                foreach (var dep in dependencies)
                {
                    _logger.LogInformation("  - {PackageId} v{Version}", dep.PackageId, dep.Version);
                }

                // Convert NuGetDependencyInfo to NuGetDependency for the Core resolver
                var coreDependencies = dependencies.Select(d => new NuGetDependency
                {
                    PackageId = d.PackageId,
                    Version = d.Version
                }).ToList();

                var logs = new List<string>();
                var resolutionResult = await _nugetResolver.ResolveAsync(coreDependencies, "net10.0", logs);

                // Log all messages from the resolver
                foreach (var log in logs)
                {
                    _logger.LogInformation("[NuGetResolver] {Log}", log);
                }

                if (resolutionResult.Success)
                {
                    _logger.LogInformation("NuGet resolution successful, found {Count} assemblies", resolutionResult.AssemblyPaths.Count);
                    
                    // Get names of assemblies we already have from standard references
                    var existingAssemblyNames = new HashSet<string>(
                        references
                            .Where(r => !string.IsNullOrEmpty(r.Display))
                            .Select(r => Path.GetFileNameWithoutExtension(r.Display!)),
                        StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var assemblyPath in resolutionResult.AssemblyPaths)
                    {
                        try
                        {
                            if (File.Exists(assemblyPath))
                            {
                                var asmName = Path.GetFileNameWithoutExtension(assemblyPath);
                                
                                // Skip if we already have this assembly from standard references
                                // This prevents version conflicts (e.g., EF Core 10.0.0 vs 10.0.1)
                                if (existingAssemblyNames.Contains(asmName))
                                {
                                    _logger.LogDebug("Skipping duplicate assembly: {Name} (already loaded)", asmName);
                                    continue;
                                }
                                
                                references.Add(MetadataReference.CreateFromFile(assemblyPath));
                                existingAssemblyNames.Add(asmName);
                                _logger.LogDebug("Added reference: {Path}", assemblyPath);
                            }
                            else
                            {
                                _logger.LogWarning("Assembly file not found: {Path}", assemblyPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load assembly: {Path}", assemblyPath);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("NuGet resolution failed: {Error}", resolutionResult.ErrorMessage);
                }
            }
            else
            {
                _logger.LogInformation("No NuGet dependencies to resolve");
            }

            // Deduplicate references
            references = references.DistinctBy(r => r.Display).ToList();
            _logger.LogInformation("Final references count after deduplication: {Count}", references.Count);

            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            result.Success = emitResult.Success;

            if (!emitResult.Success)
            {
                result.Errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error ||
                                d.Severity == DiagnosticSeverity.Warning)
                    .Select(d => new CompilationError
                    {
                        Message = d.GetMessage(),
                        Line = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                        Column = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                        Severity = d.Severity == DiagnosticSeverity.Error ? "Error" : "Warning"
                    })
                    .ToList();
            }

            _logger.LogInformation("Compilation with dependencies {Status} with {ErrorCount} issues",
                result.Success ? "succeeded" : "failed",
                result.Errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compilation with dependencies failed with exception");
            result.Success = false;
            result.Errors.Add(new CompilationError
            {
                Message = $"Compilation exception: {ex.Message}",
                Line = 1,
                Column = 1,
                Severity = "Error"
            });
        }

        return result;
    }

    /// <summary>
    /// Adds commonly required references for job code execution.
    /// </summary>
    private void AddStandardReferences(List<MetadataReference> references)
    {
        var standardAssemblyTypes = new List<Type>();

        // System.Net.Http (HttpClient)
        try
        {
            standardAssemblyTypes.Add(typeof(System.Net.Http.HttpClient));
        }
        catch { }

        // System.Net.Http.Json (HttpClientJsonExtensions)
        try
        {
            var httpJsonAssembly = Assembly.Load("System.Net.Http.Json");
            if (!string.IsNullOrEmpty(httpJsonAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(httpJsonAssembly.Location));
            }
        }
        catch { }

        // Microsoft.EntityFrameworkCore
        try
        {
            var efCoreAssembly = Assembly.Load("Microsoft.EntityFrameworkCore");
            if (!string.IsNullOrEmpty(efCoreAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(efCoreAssembly.Location));
            }
        }
        catch { }

        // Microsoft.EntityFrameworkCore.SqlServer
        try
        {
            var efSqlAssembly = Assembly.Load("Microsoft.EntityFrameworkCore.SqlServer");
            if (!string.IsNullOrEmpty(efSqlAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(efSqlAssembly.Location));
            }
        }
        catch { }

        // Microsoft.EntityFrameworkCore.Relational
        try
        {
            var efRelationalAssembly = Assembly.Load("Microsoft.EntityFrameworkCore.Relational");
            if (!string.IsNullOrEmpty(efRelationalAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(efRelationalAssembly.Location));
            }
        }
        catch { }

        // BlazorDataOrchestrator.Core
        try
        {
            var coreAssembly = typeof(BlazorDataOrchestrator.Core.JobManager).Assembly;
            if (!string.IsNullOrEmpty(coreAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(coreAssembly.Location));
            }
        }
        catch { }

        // Add type-based assemblies
        foreach (var type in standardAssemblyTypes)
        {
            try
            {
                var assembly = type.Assembly;
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            catch { }
        }

        // Additional commonly used runtime assemblies
        var additionalAssemblies = new[]
        {
            "System.ComponentModel",
            "System.ComponentModel.Primitives",
            "System.ComponentModel.Annotations",
            "System.ComponentModel.TypeConverter",  // For IListSource
            "System.Data.Common",                   // For DbConnection, DbCommand, etc.
            "System.Net.Primitives",
            "System.Net.Http",
            "System.Private.Uri",
            "System.Text.Encoding.Extensions",
            "System.Text.RegularExpressions",
            "System.Linq.Expressions",
            "System.Memory",
            "System.Buffers",
            "System.ObjectModel",
            "System.Diagnostics.DiagnosticSource",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.DependencyInjection.Abstractions"
        };

        foreach (var name in additionalAssemblies)
        {
            try
            {
                var assembly = Assembly.Load(name);
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            catch
            {
                // Assembly not available, skip
            }
        }
    }

    /// <summary>
    /// Gets metadata references needed for compilation.
    /// </summary>
    private IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();

        try
        {
            // Core runtime assemblies
            var coreAssemblies = new[]
            {
                typeof(object).Assembly,
                typeof(Console).Assembly,
                typeof(Task).Assembly,
                typeof(Enumerable).Assembly,
                typeof(List<>).Assembly
            };

            foreach (var assembly in coreAssemblies)
            {
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            // Try to load common assemblies by name
            var assemblyNames = new[]
            {
                "System.Runtime",
                "System.Collections",
                "System.Linq",
                "System.Threading.Tasks",
                "netstandard",
                "System.Console",
                "System.Text.Json"
            };

            foreach (var name in assemblyNames)
            {
                try
                {
                    var assembly = Assembly.Load(name);
                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                }
                catch
                {
                    // Assembly not available, skip
                }
            }

            // Add reference to mscorlib or System.Private.CoreLib
            var corlibLocation = typeof(object).Assembly.Location;
            var runtimeDirectory = Path.GetDirectoryName(corlibLocation);
            
            if (!string.IsNullOrEmpty(runtimeDirectory))
            {
                var systemRuntime = Path.Combine(runtimeDirectory, "System.Runtime.dll");
                if (File.Exists(systemRuntime) && !references.Any(r => r.Display?.Contains("System.Runtime") == true))
                {
                    references.Add(MetadataReference.CreateFromFile(systemRuntime));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading metadata references");
        }

        return references.DistinctBy(r => r.Display).ToList();
    }

    /// <summary>
    /// Validates C# syntax without full compilation.
    /// </summary>
    /// <param name="code">The C# code to validate.</param>
    /// <returns>Compilation result with syntax errors.</returns>
    public CompilationResult ValidateSyntax(string code)
    {
        var result = new CompilationResult { Success = true };

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var diagnostics = syntaxTree.GetDiagnostics();

            var errors = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new CompilationError
                {
                    Message = d.GetMessage(),
                    Line = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                    Column = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                    Severity = "Error"
                })
                .ToList();

            if (errors.Any())
            {
                result.Success = false;
                result.Errors = errors;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(new CompilationError
            {
                Message = $"Syntax validation exception: {ex.Message}",
                Line = 1,
                Column = 1,
                Severity = "Error"
            });
        }

        return result;
    }
}
