using BlazorDataOrchestrator.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Converts Roslyn compilation diagnostics into structured <see cref="BuildError"/> records
/// and feeds them into the <see cref="BuildErrorStore"/>.
/// 
/// This service acts as the bridge between the compilation pipeline and the
/// LLM fix orchestration system — it reads from compilation results (or structured
/// logs emitted through OpenTelemetry) and produces machine-readable error records.
/// </summary>
public class BuildTelemetryReader
{
    private readonly BuildErrorStore _errorStore;
    private readonly ILogger<BuildTelemetryReader> _logger;

    public BuildTelemetryReader(BuildErrorStore errorStore, ILogger<BuildTelemetryReader> logger)
    {
        _errorStore = errorStore;
        _logger = logger;
    }

    /// <summary>
    /// Processes Roslyn diagnostics from a compilation and records any errors in the store.
    /// </summary>
    /// <param name="diagnostics">The diagnostics from a Roslyn compilation.</param>
    /// <param name="project">The project name.</param>
    /// <param name="targetFramework">The target framework moniker.</param>
    /// <returns>The list of recorded build errors.</returns>
    public IReadOnlyList<BuildError> ProcessDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        string project,
        string targetFramework)
    {
        var errors = new List<BuildError>();

        foreach (var diagnostic in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var lineSpan = diagnostic.Location.GetLineSpan();

            var buildError = new BuildError(
                ErrorCode: diagnostic.Id,
                Message: diagnostic.GetMessage(),
                FilePath: lineSpan.Path ?? "",
                Line: lineSpan.StartLinePosition.Line + 1,
                Column: lineSpan.StartLinePosition.Character + 1,
                Project: project,
                TargetFramework: targetFramework,
                Timestamp: DateTimeOffset.UtcNow);

            errors.Add(buildError);

            _logger.LogWarning(
                "Build error [{ErrorCode}] at {File}:{Line},{Column}: {Message}",
                buildError.ErrorCode, buildError.FilePath, buildError.Line,
                buildError.Column, buildError.Message);
        }

        if (errors.Count > 0)
        {
            _errorStore.RecordErrors(errors);
            _logger.LogInformation("Recorded {Count} build error(s) for project {Project}", errors.Count, project);
        }

        return errors;
    }

    /// <summary>
    /// Processes compilation error objects (from the Web UI's CSharpCompilationService) 
    /// and records them in the store.
    /// </summary>
    /// <param name="compilationErrors">Errors from a compilation result.</param>
    /// <param name="filePath">The source file path being compiled.</param>
    /// <param name="project">The project name.</param>
    /// <param name="targetFramework">The target framework moniker.</param>
    /// <returns>The list of recorded build errors.</returns>
    public IReadOnlyList<BuildError> ProcessCompilationErrors(
        IEnumerable<(string Message, int Line, int Column, string Severity)> compilationErrors,
        string filePath,
        string project,
        string targetFramework)
    {
        var errors = new List<BuildError>();

        foreach (var (message, line, column, severity) in compilationErrors)
        {
            if (!severity.Equals("Error", StringComparison.OrdinalIgnoreCase))
                continue;

            // Try to extract the error code from the message (e.g., "error CS1061: ...")
            var errorCode = ExtractErrorCode(message);

            var buildError = new BuildError(
                ErrorCode: errorCode,
                Message: message,
                FilePath: filePath,
                Line: line,
                Column: column,
                Project: project,
                TargetFramework: targetFramework,
                Timestamp: DateTimeOffset.UtcNow);

            errors.Add(buildError);
        }

        if (errors.Count > 0)
        {
            _errorStore.RecordErrors(errors);
            _logger.LogInformation("Recorded {Count} compilation error(s) for {File}", errors.Count, filePath);
        }

        return errors;
    }

    /// <summary>
    /// Gets the latest build errors from the store.
    /// </summary>
    public IReadOnlyList<BuildError> GetLatestErrors(int count = 20, string? project = null)
    {
        return _errorStore.GetLatest(count, project);
    }

    /// <summary>
    /// Extracts an error code (e.g., "CS1061") from a diagnostic message.
    /// </summary>
    private string ExtractErrorCode(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\b(CS\d{4})\b");
        return match.Success ? match.Groups[1].Value : "CS0000";
    }
}
