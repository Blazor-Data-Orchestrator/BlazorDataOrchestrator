namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Represents a structured build error captured from compilation diagnostics.
/// </summary>
public sealed record BuildError(
    string ErrorCode,
    string Message,
    string FilePath,
    int Line,
    int Column,
    string Project,
    string TargetFramework,
    DateTimeOffset Timestamp);

/// <summary>
/// Categories for classifying why the LLM failed to fix a build error.
/// </summary>
public enum RootCauseCategory
{
    /// <summary>Not yet classified.</summary>
    Unknown = 0,

    /// <summary>The LLM did not have the type's public API surface in context.</summary>
    MissingTypeInfo,

    /// <summary>A required NuGet package or SDK API was not provided.</summary>
    MissingNuGetApi,

    /// <summary>Only the erroring file was sent — callers / dependents were missing.</summary>
    InsufficientFileContext,

    /// <summary>The LLM fabricated a method or property that does not exist.</summary>
    HallucinatedApi,

    /// <summary>The fix targets the wrong Target Framework Moniker.</summary>
    WrongTargetFramework,

    /// <summary>The prompt was ambiguous, leading to multiple or hedged fixes.</summary>
    PromptAmbiguity
}

/// <summary>
/// Records a single LLM fix attempt with full prompt, response, and outcome.
/// Stored in-memory for runtime analysis — no external database dependency.
/// </summary>
public sealed record FixAttempt(
    Guid Id,
    BuildError OriginalError,
    string PromptSent,
    string LlmResponse,
    bool RebuildSucceeded,
    BuildError? ResidualError,
    RootCauseCategory RootCauseCategory,
    DateTimeOffset Timestamp);

/// <summary>
/// A negative example learned from past LLM failures — used to prevent repeat hallucinations.
/// </summary>
public sealed record NegativeExample(
    string ErrorCode,
    string BadSymbol,
    string CorrectAlternative,
    DateTimeOffset LearnedAt);

/// <summary>
/// Context assembled by the ContextGatherer for prompt construction.
/// </summary>
public sealed class BuildErrorContext
{
    /// <summary>The build error being addressed.</summary>
    public required BuildError Error { get; init; }

    /// <summary>Full text of the source file containing the error.</summary>
    public required string SourceFileContent { get; init; }

    /// <summary>Summaries of related types (public members only).</summary>
    public List<TypeSummary> RelatedTypes { get; init; } = [];

    /// <summary>NuGet packages referenced by the project.</summary>
    public List<PackageInfo> Packages { get; init; } = [];

    /// <summary>The project name.</summary>
    public required string ProjectName { get; init; }

    /// <summary>The target framework moniker (e.g. net10.0).</summary>
    public required string TargetFramework { get; init; }

    /// <summary>Aspire version used by the solution.</summary>
    public string AspireVersion { get; init; } = "";

    /// <summary>Negative examples for the error code, if any.</summary>
    public List<NegativeExample> NegativeExamples { get; init; } = [];
}

/// <summary>
/// Summary of a type's public API surface for LLM context.
/// </summary>
public sealed record TypeSummary(
    string FullName,
    string PublicMemberSummary);

/// <summary>
/// Information about a NuGet package.
/// </summary>
public sealed record PackageInfo(
    string Id,
    string Version);
