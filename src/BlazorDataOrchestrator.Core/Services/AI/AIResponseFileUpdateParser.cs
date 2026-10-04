using System.Text.RegularExpressions;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services.AI;

/// <summary>
/// Converts an AI response into validated, file-targeted updates.
/// <para>
/// <c>###UPDATED CODE BEGIN###</c> always targets the PRIMARY code file (main.cs / main.py) — never the
/// file that happens to be open in the editor. Other files must be named explicitly with
/// <c>###UPDATED FILE BEGIN: name###</c>, and the name must be one of the job's files.
/// </para>
/// </summary>
public static partial class AIResponseFileUpdateParser
{
    public const string CodeBeginMarker = "###UPDATED CODE BEGIN###";
    public const string CodeEndMarker = "###UPDATED CODE END###";
    public const string FileBeginMarkerPrefix = "###UPDATED FILE BEGIN:";
    public const string FileEndMarker = "###UPDATED FILE END###";
    public const string NuspecBeginMarker = "###NUSPEC BEGIN###";
    public const string NuspecEndMarker = "###NUSPEC END###";
    public const string DefaultNuspecFileName = "BlazorDataOrchestrator.Job.nuspec";

    private sealed record Candidate(int Index, string FileName, string Content, FileUpdateSource Source);

    /// <param name="response">The complete AI response.</param>
    /// <param name="context">The editor context the request was sent with.</param>
    /// <param name="previousContents">Current file contents keyed by file name; defaults to the contents in <paramref name="context"/>.</param>
    /// <param name="allowFencedFallback">
    /// When true and the response has no markers, a complete-looking fenced code block is proposed for the primary
    /// file (or a JSON block for the active .json file). Callers that apply updates without user confirmation should pass false.
    /// </param>
    public static AIResponseParseResult Parse(
        string? response,
        AIEditorContext context,
        IReadOnlyDictionary<string, string>? previousContents = null,
        bool allowFencedFallback = true)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return AIResponseParseResult.Empty;
        }

        var text = response.Replace("\r\n", "\n").Replace('\r', '\n');
        var warnings = new List<string>();
        var candidates = new List<Candidate>();

        // NUSPEC blocks can appear anywhere (including inside the code markers); take them out first
        // so they never end up inside main.cs.
        foreach (Match match in NuspecBlockRegex().Matches(text))
        {
            if (context.IsPython)
            {
                warnings.Add("Ignored a .nuspec block: Python jobs use requirements.txt.");
                continue;
            }

            var nuspecName = context.NuspecFileName ?? context.ResolveFileName(DefaultNuspecFileName) ?? DefaultNuspecFileName;
            candidates.Add(new Candidate(match.Index, nuspecName, StripFence(match.Groups["body"].Value), FileUpdateSource.NuspecMarkers));
        }
        var withoutNuspec = NuspecBlockRegex().Replace(text, match => new string(' ', match.Length));

        foreach (Match match in PrimaryBlockRegex().Matches(withoutNuspec))
        {
            candidates.Add(new Candidate(match.Index, context.PrimaryCodeFileName, StripFence(match.Groups["body"].Value), FileUpdateSource.PrimaryCodeMarkers));
        }

        foreach (Match match in NamedBlockRegex().Matches(withoutNuspec))
        {
            var requestedName = match.Groups["name"].Value.Trim();
            var resolved = context.ResolveFileName(requestedName);
            if (resolved is null)
            {
                warnings.Add($"Ignored an update for '{requestedName}', which is not a file in this job.");
                continue;
            }
            candidates.Add(new Candidate(match.Index, resolved, StripFence(match.Groups["body"].Value), FileUpdateSource.NamedFileMarkers));
        }

        if (HasUnclosedMarker(text))
        {
            warnings.Add("The response appears to be truncated: a file block was opened but never closed. Ask the AI to resend the complete file.");
        }

        if (allowFencedFallback && candidates.Count == 0 && !ContainsAnyMarker(text))
        {
            var fallback = FindFallbackCandidate(text, context);
            if (fallback != null)
            {
                candidates.Add(fallback);
            }
        }

        var updates = new List<ProposedFileUpdate>();
        foreach (var group in candidates.OrderBy(c => c.Index).GroupBy(c => c.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var chosen = group.Last();
            if (group.Count() > 1)
            {
                warnings.Add($"The response contained {group.Count()} versions of {chosen.FileName}; the last one is used.");
            }

            var previous = GetPreviousContent(chosen.FileName, context, previousContents);
            var isPrimary = string.Equals(chosen.FileName, context.PrimaryCodeFileName, StringComparison.OrdinalIgnoreCase);
            var validation = JobFileContentValidator.Validate(chosen.FileName, chosen.Content, previous, isPrimary);
            updates.Add(new ProposedFileUpdate(chosen.FileName, chosen.Content, chosen.Source, validation));
        }

        // Show the primary code file first; it is the one the editor switches to.
        var ordered = updates
            .OrderByDescending(u => string.Equals(u.FileName, context.PrimaryCodeFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return new AIResponseParseResult(ordered, warnings);
    }

    /// <summary>Removes the marker lines so the chat shows clean markdown.</summary>
    public static string StripMarkersForDisplay(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        var cleaned = NamedMarkerForDisplayRegex().Replace(content, m => $"**{m.Groups["name"].Value.Trim()}**\n");
        cleaned = MarkerRegex().Replace(cleaned, string.Empty);
        return ExtraBlankLinesRegex().Replace(cleaned, "\n\n");
    }

    private static Candidate? FindFallbackCandidate(string text, AIEditorContext context)
    {
        var codeLanguages = context.IsPython
            ? new[] { "python", "py" }
            : new[] { "csharp", "cs", "c#" };

        var codeBlocks = FencedBlockRegex().Matches(text)
            .Where(m => codeLanguages.Contains(m.Groups["lang"].Value.Trim().ToLowerInvariant()))
            .Select(m => (m.Index, Body: m.Groups["body"].Value.Trim()))
            .ToList();

        if (codeBlocks.Count == 0)
        {
            // A truncated response may leave the last fence unclosed.
            var unclosed = UnclosedFencedBlockRegex().Match(text);
            if (unclosed.Success && codeLanguages.Contains(unclosed.Groups["lang"].Value.Trim().ToLowerInvariant()))
            {
                codeBlocks.Add((unclosed.Index, unclosed.Groups["body"].Value.Trim()));
            }
        }

        var best = codeBlocks
            .Where(b => LooksLikeCompleteCodeFile(b.Body))
            .OrderByDescending(b => b.Body.Length)
            .FirstOrDefault();
        if (best.Body != null)
        {
            return new Candidate(best.Index, context.PrimaryCodeFileName, best.Body, FileUpdateSource.FencedBlockFallback);
        }

        // A bare ```json block is only applied to the ACTIVE file, and only when that file is JSON.
        if (JobFileTypes.GetKind(context.ActiveFileName) == JobFileKind.Json)
        {
            var json = FencedBlockRegex().Matches(text)
                .Where(m => m.Groups["lang"].Value.Trim().Equals("json", StringComparison.OrdinalIgnoreCase))
                .Select(m => (m.Index, Body: m.Groups["body"].Value.Trim()))
                .OrderByDescending(b => b.Body.Length)
                .FirstOrDefault();
            if (json.Body != null && json.Body.StartsWith('{'))
            {
                return new Candidate(json.Index, context.ActiveFileName, json.Body, FileUpdateSource.FencedBlockFallback);
            }
        }

        return null;
    }

    private static bool LooksLikeCompleteCodeFile(string code) =>
        code.Contains("class ", StringComparison.Ordinal) ||
        code.Contains("def ", StringComparison.Ordinal) ||
        code.Contains("public static", StringComparison.Ordinal) ||
        code.Contains("namespace ", StringComparison.Ordinal) ||
        code.Split('\n').Length >= 10;

    private static string? GetPreviousContent(string fileName, AIEditorContext context, IReadOnlyDictionary<string, string>? previousContents)
    {
        if (previousContents != null)
        {
            foreach (var pair in previousContents)
            {
                if (string.Equals(pair.Key, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return context.GetFileContent(fileName);
    }

    /// <summary>Strips one surrounding markdown code fence from a block body.</summary>
    internal static string StripFence(string body)
    {
        var code = body.Trim();
        code = OpeningFenceRegex().Replace(code, string.Empty, 1);
        code = ClosingFenceRegex().Replace(code, string.Empty, 1);
        return code.Trim();
    }

    private static bool ContainsAnyMarker(string text) =>
        text.Contains(CodeBeginMarker, StringComparison.Ordinal) ||
        text.Contains(FileBeginMarkerPrefix, StringComparison.Ordinal) ||
        text.Contains(NuspecBeginMarker, StringComparison.Ordinal);

    // Real markers start a line (optionally indented). Requiring that ignores markers that are merely
    // quoted mid-line, e.g. inside a string literal in a code example.
    private static bool HasUnclosedMarker(string text) =>
        CodeBeginLine.Matches(text).Count > CodeEndLine.Matches(text).Count ||
        FileBeginLine.Matches(text).Count > FileEndLine.Matches(text).Count ||
        NuspecBeginLine.Matches(text).Count > NuspecEndLine.Matches(text).Count;

    private static readonly Regex CodeBeginLine = LineMarkerRegex(CodeBeginMarker);
    private static readonly Regex CodeEndLine = LineMarkerRegex(CodeEndMarker);
    private static readonly Regex FileBeginLine = LineMarkerRegex(FileBeginMarkerPrefix);
    private static readonly Regex FileEndLine = LineMarkerRegex(FileEndMarker);
    private static readonly Regex NuspecBeginLine = LineMarkerRegex(NuspecBeginMarker);
    private static readonly Regex NuspecEndLine = LineMarkerRegex(NuspecEndMarker);

    private static Regex LineMarkerRegex(string marker) =>
        new($@"^[ \t]*{Regex.Escape(marker)}", RegexOptions.Multiline | RegexOptions.Compiled);

    [GeneratedRegex(@"^[ \t]*###UPDATED CODE BEGIN###(?<body>.*?)^[ \t]*###UPDATED CODE END###", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex PrimaryBlockRegex();

    [GeneratedRegex(@"^[ \t]*###UPDATED FILE BEGIN:[ \t]*(?<name>[^#\n]+?)[ \t]*###(?<body>.*?)^[ \t]*###UPDATED FILE END###", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex NamedBlockRegex();

    [GeneratedRegex(@"^[ \t]*###NUSPEC BEGIN###(?<body>.*?)^[ \t]*###NUSPEC END###", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex NuspecBlockRegex();

    [GeneratedRegex(@"```[ \t]*(?<lang>[\w#+-]*)[ \t]*\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex FencedBlockRegex();

    [GeneratedRegex(@"```[ \t]*(?<lang>[\w#+-]+)[ \t]*\n(?<body>[^`]+)$", RegexOptions.Singleline)]
    private static partial Regex UnclosedFencedBlockRegex();

    [GeneratedRegex(@"\A```[ \t]*[\w#+-]*[ \t]*\n?")]
    private static partial Regex OpeningFenceRegex();

    [GeneratedRegex(@"\n?```[ \t]*\z")]
    private static partial Regex ClosingFenceRegex();

    [GeneratedRegex(@"###UPDATED FILE BEGIN:[ \t]*(?<name>[^#\r\n]+?)[ \t]*###[ \t]*\r?\n?")]
    private static partial Regex NamedMarkerForDisplayRegex();

    [GeneratedRegex(@"###(UPDATED CODE (BEGIN|END)|UPDATED FILE END|NUSPEC (BEGIN|END))###[ \t]*\r?\n?")]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex ExtraBlankLinesRegex();
}
