using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BlazorDataOrchestrator.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorDataOrchestrator.Core.Services.AI;

/// <summary>
/// Validates proposed content for a job file based on the file's type, so that code can never
/// be written into a .json file and every .json file stays strictly valid JSON.
/// </summary>
public static partial class JobFileContentValidator
{
    /// <summary>Reserved connection strings that the executing host always overwrites.</summary>
    public static readonly IReadOnlyList<string> ReservedConnectionStrings = ["blobs", "queues", "tables", "blazororchestratordb"];

    /// <summary>Strict RFC 8259 parsing: no comments, no trailing commas (matches Python's json.loads).</summary>
    public static readonly JsonDocumentOptions StrictJsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    /// <param name="fileName">Target file name, e.g. <c>appsettings.json</c>.</param>
    /// <param name="content">The complete proposed content.</param>
    /// <param name="previousContent">The file's current content, used to detect accidental removals.</param>
    /// <param name="isPrimaryCodeFile">True for <c>main.cs</c> / <c>main.py</c>; enables entry-point checks.</param>
    public static FileValidationResult Validate(
        string fileName,
        string? content,
        string? previousContent = null,
        bool isPrimaryCodeFile = false)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return FileValidationResult.Fail($"{fileName} would be empty.");
        }

        var detected = ContentKindDetector.Detect(content);
        return JobFileTypes.GetKind(fileName) switch
        {
            JobFileKind.Json => ValidateJson(fileName, content, previousContent, detected),
            JobFileKind.CSharp => ValidateCSharp(fileName, content, detected, isPrimaryCodeFile),
            JobFileKind.Python => ValidatePython(fileName, content, detected, isPrimaryCodeFile),
            JobFileKind.Nuspec => ValidateNuspec(fileName, content, detected),
            JobFileKind.Requirements => ValidateRequirements(fileName, content, detected),
            _ => FileValidationResult.Ok()
        };
    }

    /// <summary>
    /// Returns null when <paramref name="content"/> is strictly valid JSON with an object root, otherwise a
    /// user-facing error message that includes the line and position.
    /// </summary>
    public static string? GetJsonError(string fileName, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"{fileName} is empty. It must contain a JSON object, for example {{ }}.";
        }

        try
        {
            using var document = JsonDocument.Parse(content, StrictJsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? null
                : $"{fileName} must contain a single JSON object ({{ ... }}), not a {document.RootElement.ValueKind}.";
        }
        catch (JsonException ex)
        {
            var line = (ex.LineNumber ?? 0) + 1;
            var position = (ex.BytePositionInLine ?? 0) + 1;
            return $"{fileName} is not valid JSON (line {line}, position {position}): {CleanJsonMessage(ex.Message)}";
        }
    }

    private static FileValidationResult ValidateJson(string fileName, string content, string? previousContent, ContentKind detected)
    {
        if (detected is ContentKind.CSharp or ContentKind.Python or ContentKind.Xml)
        {
            return FileValidationResult.Fail(
                $"{ContentKindDetector.Describe(detected)} cannot be written to {fileName}. Only JSON settings belong in .json files.");
        }

        var jsonError = GetJsonError(fileName, content);
        if (jsonError != null)
        {
            return FileValidationResult.Fail(jsonError);
        }

        var warnings = GetReservedConnectionStringWarnings(fileName, content, previousContent).ToList();
        var removed = GetRemovedSettingPaths(content, previousContent)
            .Where(path => !ReservedConnectionStrings.Any(r => string.Equals(path, $"ConnectionStrings:{r}", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (removed.Count > 0)
        {
            warnings.Add($"{fileName}: this change removes existing settings: {string.Join(", ", removed.Take(10))}{(removed.Count > 10 ? ", …" : "")}.");
        }

        return FileValidationResult.Ok(warnings.ToArray());
    }

    /// <summary>
    /// Lists settings (up to two levels deep, e.g. <c>Logging</c> or <c>WeatherApi:ApiKey</c>) that exist in
    /// <paramref name="previousContent"/> but not in <paramref name="content"/>.
    /// </summary>
    internal static IReadOnlyList<string> GetRemovedSettingPaths(string content, string? previousContent)
    {
        if (string.IsNullOrWhiteSpace(previousContent))
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(previousContent, documentOptions: StrictJsonOptions) is not JsonObject before ||
                JsonNode.Parse(content, documentOptions: StrictJsonOptions) is not JsonObject after)
            {
                return [];
            }

            var removed = new List<string>();
            foreach (var (key, value) in before)
            {
                var match = FindProperty(after, key);
                if (match is null)
                {
                    removed.Add(key);
                    continue;
                }

                if (value is JsonObject beforeSection && match is JsonObject afterSection)
                {
                    removed.AddRange(beforeSection
                        .Where(child => FindProperty(afterSection, child.Key) is null)
                        .Select(child => $"{key}:{child.Key}"));
                }
            }
            return removed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonNode? FindProperty(JsonObject obj, string key)
    {
        foreach (var (name, value) in obj)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                // A property present with a null value still counts as present.
                return value ?? JsonValue.Create(string.Empty);
            }
        }
        return null;
    }

    private static IEnumerable<string> GetReservedConnectionStringWarnings(string fileName, string content, string? previousContent)
    {
        if (!Path.GetFileName(fileName).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var current = ReadConnectionStrings(content);
        var previous = ReadConnectionStrings(previousContent);

        foreach (var key in ReservedConnectionStrings)
        {
            var hadKey = previous?.ContainsKey(key) == true;
            string? newValue = null;
            var hasKey = current?.TryGetValue(key, out newValue) == true;

            if (hadKey && !hasKey)
            {
                yield return $"{fileName}: the reserved connection string '{key}' was removed. Keep it present and blank.";
            }
            else if (hasKey && !string.IsNullOrEmpty(newValue) &&
                     !string.Equals(newValue, previous?.GetValueOrDefault(key), StringComparison.Ordinal))
            {
                yield return $"{fileName}: the reserved connection string '{key}' was given a value. The host overwrites it, so leave it blank.";
            }
        }
    }

    private static Dictionary<string, string?>? ReadConnectionStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(json, documentOptions: StrictJsonOptions) is not JsonObject root)
            {
                return null;
            }

            var section = root.FirstOrDefault(p => string.Equals(p.Key, "ConnectionStrings", StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
            if (section is null)
            {
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }

            return section.ToDictionary(
                p => p.Key,
                p => p.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : p.Value?.ToJsonString(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FileValidationResult ValidateCSharp(string fileName, string content, ContentKind detected, bool isPrimaryCodeFile)
    {
        if (detected is ContentKind.Json or ContentKind.Xml or ContentKind.Python)
        {
            return FileValidationResult.Fail(
                $"{ContentKindDetector.Describe(detected)} cannot be written to {fileName}. Only C# code belongs in .cs files.");
        }

        var warnings = new List<string>();
        var syntaxErrors = CSharpSyntaxTree.ParseText(content)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Take(3)
            .Select(d => $"{fileName} line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
            .ToList();
        if (syntaxErrors.Count > 0)
        {
            warnings.Add($"{fileName} has C# syntax errors that will fail compilation:");
            warnings.AddRange(syntaxErrors);
        }

        if (isPrimaryCodeFile)
        {
            if (!content.Contains("class BlazorDataOrchestratorJob", StringComparison.Ordinal))
            {
                warnings.Add($"{fileName} does not define the class BlazorDataOrchestratorJob.");
            }
            if (!content.Contains("ExecuteJob(", StringComparison.Ordinal))
            {
                warnings.Add($"{fileName} does not define the ExecuteJob method.");
            }
        }

        return FileValidationResult.Ok(warnings.ToArray());
    }

    private static FileValidationResult ValidatePython(string fileName, string content, ContentKind detected, bool isPrimaryCodeFile)
    {
        if (detected is ContentKind.Json or ContentKind.Xml or ContentKind.CSharp)
        {
            return FileValidationResult.Fail(
                $"{ContentKindDetector.Describe(detected)} cannot be written to {fileName}. Only Python code belongs in .py files.");
        }

        if (isPrimaryCodeFile && !PythonExecuteJobRegex().IsMatch(content))
        {
            return FileValidationResult.Ok($"{fileName} does not define the execute_job function.");
        }

        return FileValidationResult.Ok();
    }

    private static FileValidationResult ValidateNuspec(string fileName, string content, ContentKind detected)
    {
        if (detected is ContentKind.CSharp or ContentKind.Python or ContentKind.Json)
        {
            return FileValidationResult.Fail(
                $"{ContentKindDetector.Describe(detected)} cannot be written to {fileName}. Only the NuGet XML manifest belongs in .nuspec files.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            return FileValidationResult.Fail($"{fileName} is not valid XML (line {ex.LineNumber}, position {ex.LinePosition}): {ex.Message}");
        }

        if (document.Root?.Name.LocalName != "package")
        {
            return FileValidationResult.Fail($"{fileName} must have a <package> root element.");
        }

        var warnings = new List<string>();
        var dependencies = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (dependencies is null)
        {
            warnings.Add($"{fileName} has no <dependencies> element.");
        }
        else
        {
            var frameworks = dependencies.Elements()
                .Where(e => e.Name.LocalName == "group")
                .Select(e => (string?)e.Attribute("targetFramework"))
                .Where(f => !string.IsNullOrEmpty(f) && !string.Equals(f, "net10.0", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (frameworks.Count > 0)
            {
                warnings.Add($"{fileName}: targetFramework should be net10.0 (found {string.Join(", ", frameworks)}).");
            }
        }

        return FileValidationResult.Ok(warnings.ToArray());
    }

    private static FileValidationResult ValidateRequirements(string fileName, string content, ContentKind detected)
    {
        if (detected is ContentKind.CSharp or ContentKind.Python or ContentKind.Json or ContentKind.Xml)
        {
            return FileValidationResult.Fail(
                $"{ContentKindDetector.Describe(detected)} cannot be written to {fileName}. It may only list packages, one per line.");
        }

        var warnings = new List<string>();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('-'))
            {
                continue;
            }

            if (!RequirementLineRegex().IsMatch(line))
            {
                warnings.Add($"{fileName} line {i + 1} does not look like a package requirement: {line}");
            }
            else if (!line.Contains("==", StringComparison.Ordinal))
            {
                warnings.Add($"{fileName} line {i + 1} should pin an exact version with '==': {line}");
            }
        }

        return FileValidationResult.Ok(warnings.ToArray());
    }

    private static string CleanJsonMessage(string message)
    {
        var index = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return index > 0 ? message[..index] : message;
    }

    [GeneratedRegex(@"^\s*(async\s+)?def\s+execute_job\s*\(", RegexOptions.Multiline)]
    private static partial Regex PythonExecuteJobRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*(\[[A-Za-z0-9_,.\- ]+\])?\s*((==|>=|<=|~=|!=|===|>|<)\s*[A-Za-z0-9_.*+!\-]+\s*,?\s*)*(;.*)?$")]
    private static partial Regex RequirementLineRegex();
}
