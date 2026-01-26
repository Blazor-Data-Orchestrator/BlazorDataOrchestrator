using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for compiling and validating C# code.
/// </summary>
public class CSharpCompilationService
{
    private readonly ILogger<CSharpCompilationService> _logger;

    public CSharpCompilationService(ILogger<CSharpCompilationService> logger)
    {
        _logger = logger;
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
