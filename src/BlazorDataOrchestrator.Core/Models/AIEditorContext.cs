namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Describes what the user has open in the code editor so the AI knows which file is active,
/// which file holds the job code, and which files exist.
/// </summary>
public sealed record AIEditorContext
{
    public const string CSharpPrimaryFileName = "main.cs";
    public const string PythonPrimaryFileName = "main.py";

    /// <summary>"csharp" or "python".</summary>
    public required string Language { get; init; }

    /// <summary>The file currently open in the editor, e.g. <c>appsettings.json</c>.</summary>
    public required string ActiveFileName { get; init; }

    /// <summary>The current editor buffer of <see cref="ActiveFileName"/>.</summary>
    public required string ActiveFileContent { get; init; }

    /// <summary>The job's main code file, <c>main.cs</c> or <c>main.py</c>.</summary>
    public required string PrimaryCodeFileName { get; init; }

    public required string PrimaryCodeContent { get; init; }

    /// <summary>Every file name the user can edit; AI updates may only target these.</summary>
    public IReadOnlyList<string> AvailableFiles { get; init; } = [];

    public string? NuspecFileName { get; init; }

    public string? NuspecContent { get; init; }

    /// <summary>
    /// Current contents of the job's other files (for example the appsettings files and requirements.txt),
    /// keyed by file name, so the AI edits real content instead of re-creating a file from scratch.
    /// The active, primary and nuspec files are excluded because they have their own properties.
    /// </summary>
    public IReadOnlyDictionary<string, string> OtherFileContents { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool ActiveIsPrimary =>
        string.Equals(ActiveFileName, PrimaryCodeFileName, StringComparison.OrdinalIgnoreCase);

    public bool IsPython => IsPythonLanguage(Language);

    public static bool IsPythonLanguage(string? language) =>
        string.Equals(language?.Trim(), "python", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language?.Trim(), "py", StringComparison.OrdinalIgnoreCase);

    public static string GetPrimaryFileName(string? language) =>
        IsPythonLanguage(language) ? PythonPrimaryFileName : CSharpPrimaryFileName;

    /// <summary>
    /// Context for callers that only have a single code buffer: the buffer is treated as the primary code file.
    /// </summary>
    public static AIEditorContext ForSingleFile(string? language, string? code)
    {
        var normalizedLanguage = IsPythonLanguage(language) ? "python" : "csharp";
        var primary = GetPrimaryFileName(normalizedLanguage);
        return new AIEditorContext
        {
            Language = normalizedLanguage,
            ActiveFileName = primary,
            ActiveFileContent = code ?? "",
            PrimaryCodeFileName = primary,
            PrimaryCodeContent = code ?? "",
            AvailableFiles = [primary]
        };
    }

    /// <summary>
    /// Returns the current content of <paramref name="fileName"/> from the context, or null when it is unknown.
    /// </summary>
    public string? GetFileContent(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        if (string.Equals(fileName, ActiveFileName, StringComparison.OrdinalIgnoreCase)) return ActiveFileContent;
        if (string.Equals(fileName, PrimaryCodeFileName, StringComparison.OrdinalIgnoreCase)) return PrimaryCodeContent;
        if (string.Equals(fileName, NuspecFileName, StringComparison.OrdinalIgnoreCase)) return NuspecContent;
        foreach (var pair in OtherFileContents)
        {
            if (string.Equals(pair.Key, fileName, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    }

    /// <summary>
    /// Returns the canonical name from <see cref="AvailableFiles"/> that matches <paramref name="fileName"/>, or null.
    /// The primary and nuspec files are always considered available.
    /// </summary>
    public string? ResolveFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var trimmed = fileName.Trim().Trim('`', '"', '\'');
        var candidates = AvailableFiles
            .Append(PrimaryCodeFileName)
            .Append(ActiveFileName);
        if (!string.IsNullOrEmpty(NuspecFileName))
        {
            candidates = candidates.Append(NuspecFileName);
        }

        return candidates.FirstOrDefault(f => string.Equals(f, trimmed, StringComparison.OrdinalIgnoreCase))
            // Allow a path prefix such as "CodeCSharp/main.cs" as long as the file name matches.
            ?? candidates.FirstOrDefault(f => string.Equals(f, Path.GetFileName(trimmed), StringComparison.OrdinalIgnoreCase));
    }
}
