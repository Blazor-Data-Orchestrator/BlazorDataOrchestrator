namespace BlazorDataOrchestrator.Core.Models;

/// <summary>Where in the AI response a proposed file update came from.</summary>
public enum FileUpdateSource
{
    /// <summary><c>###UPDATED CODE BEGIN###</c> — always targets the primary code file.</summary>
    PrimaryCodeMarkers,

    /// <summary><c>###UPDATED FILE BEGIN: name###</c>.</summary>
    NamedFileMarkers,

    /// <summary><c>###NUSPEC BEGIN###</c>.</summary>
    NuspecMarkers,

    /// <summary>A fenced code block without markers.</summary>
    FencedBlockFallback
}

/// <summary>The kind of job file, derived from its extension.</summary>
public enum JobFileKind
{
    CSharp,
    Python,
    Json,
    Nuspec,
    Requirements,
    Other
}

/// <summary>Result of validating proposed content for a specific file.</summary>
public sealed record FileValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static FileValidationResult Ok(params string[] warnings) => new(true, [], warnings);

    public static FileValidationResult Fail(params string[] errors) => new(false, errors, []);
}

/// <summary>A complete replacement for one job file, proposed by the AI and validated for its file type.</summary>
public sealed record ProposedFileUpdate(
    string FileName,
    string Content,
    FileUpdateSource Source,
    FileValidationResult Validation)
{
    public bool IsValid => Validation.IsValid;
}

/// <summary>All file updates found in an AI response.</summary>
public sealed record AIResponseParseResult(
    IReadOnlyList<ProposedFileUpdate> Updates,
    IReadOnlyList<string> Warnings)
{
    public static AIResponseParseResult Empty { get; } = new([], []);

    public IReadOnlyList<ProposedFileUpdate> ValidUpdates => Updates.Where(u => u.IsValid).ToList();

    public IReadOnlyList<ProposedFileUpdate> InvalidUpdates => Updates.Where(u => !u.IsValid).ToList();

    public bool HasApplicableUpdates => Updates.Any(u => u.IsValid);
}
