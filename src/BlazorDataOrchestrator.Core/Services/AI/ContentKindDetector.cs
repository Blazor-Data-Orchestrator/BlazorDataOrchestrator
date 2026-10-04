using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorDataOrchestrator.Core.Services.AI;

/// <summary>What a piece of text looks like, independent of the file it is destined for.</summary>
public enum ContentKind
{
    Unknown,
    Json,
    Xml,
    CSharp,
    Python
}

/// <summary>
/// Heuristic detection of JSON / XML / C# / Python content. Only strong signals are used so
/// that a valid file is not mistaken for another type.
/// </summary>
public static partial class ContentKindDetector
{
    public static ContentKind Detect(string? content)
    {
        var text = content?.Trim().TrimStart('\uFEFF') ?? string.Empty;
        if (text.Length == 0)
        {
            return ContentKind.Unknown;
        }

        if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<package", StringComparison.OrdinalIgnoreCase))
        {
            return ContentKind.Xml;
        }

        if (text[0] is '{' or '[')
        {
            if (IsJson(text) || (text[0] == '{' && JsonPropertyRegex().IsMatch(text)))
            {
                return ContentKind.Json;
            }
        }

        if (CSharpUsingRegex().IsMatch(text) ||
            CSharpNamespaceRegex().IsMatch(text) ||
            CSharpTypeRegex().IsMatch(text) ||
            CSharpNuGetHeaderRegex().IsMatch(text) ||
            CSharpAsyncTaskRegex().IsMatch(text))
        {
            return ContentKind.CSharp;
        }

        if (PythonDefRegex().IsMatch(text) ||
            PythonImportRegex().IsMatch(text) ||
            PythonRequirementsHeaderRegex().IsMatch(text) ||
            PythonClassRegex().IsMatch(text))
        {
            return ContentKind.Python;
        }

        return ContentKind.Unknown;
    }

    public static string Describe(ContentKind kind) => kind switch
    {
        ContentKind.CSharp => "C# code",
        ContentKind.Python => "Python code",
        ContentKind.Json => "JSON",
        ContentKind.Xml => "XML",
        _ => "Unrecognised content"
    };

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, JobFileContentValidator.StrictJsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^\s*""[^""\r\n]+""\s*:", RegexOptions.Multiline)]
    private static partial Regex JsonPropertyRegex();

    [GeneratedRegex(@"^\s*using\s+(static\s+)?[\w.]+(\s*=\s*[\w.<>]+)?\s*;", RegexOptions.Multiline)]
    private static partial Regex CSharpUsingRegex();

    [GeneratedRegex(@"^\s*namespace\s+[\w.]+\s*[;{]?\s*$", RegexOptions.Multiline)]
    private static partial Regex CSharpNamespaceRegex();

    [GeneratedRegex(@"\b(public|internal|private|protected)\s+((static|sealed|abstract|partial)\s+)*(class|record|interface|struct|enum)\s+\w+")]
    private static partial Regex CSharpTypeRegex();

    [GeneratedRegex(@"^\s*//\s*(REQUIRES\s+)?NUGET:", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex CSharpNuGetHeaderRegex();

    [GeneratedRegex(@"\b(public|private|internal)\s+(static\s+)?async\s+Task\b")]
    private static partial Regex CSharpAsyncTaskRegex();

    [GeneratedRegex(@"^\s*(async\s+)?def\s+\w+\s*\(", RegexOptions.Multiline)]
    private static partial Regex PythonDefRegex();

    [GeneratedRegex(@"^(from\s+[\w.]+\s+import\s+[\w.*(]|import\s+[\w.]+(\s+as\s+\w+)?\s*$)", RegexOptions.Multiline)]
    private static partial Regex PythonImportRegex();

    [GeneratedRegex(@"^\s*#\s*ADD TO REQUIREMENTS", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PythonRequirementsHeaderRegex();

    [GeneratedRegex(@"^class\s+\w+(\([^)]*\))?\s*:\s*$", RegexOptions.Multiline)]
    private static partial Regex PythonClassRegex();
}
