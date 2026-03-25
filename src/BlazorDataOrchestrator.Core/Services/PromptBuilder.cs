using BlazorDataOrchestrator.Core.Models;
using System.Text;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Builds a structured, multi-section prompt for the LLM to fix build errors.
/// Implements the six-section template design from the LLM Build Error Resolution Plan:
///   1. System Role
///   2. Project Metadata
///   3. Build Error
///   4. Source File
///   5. Available API Surface
///   6. Output Format Constraint (+ Negative Examples)
/// 
/// Enforces a configurable token budget by progressively trimming context.
/// </summary>
public class PromptBuilder
{
    private readonly int _tokenBudget;

    /// <summary>
    /// Approximate characters per token for budget estimation (conservative).
    /// </summary>
    private const int CharsPerToken = 4;

    public PromptBuilder(int tokenBudget = 12_000)
    {
        _tokenBudget = tokenBudget;
    }

    /// <summary>
    /// Assembles the full prompt from the gathered context.
    /// </summary>
    public string BuildPrompt(BuildErrorContext context)
    {
        var sb = new StringBuilder();

        // Section 1 — System Role
        AppendSystemRole(sb);

        // Section 2 — Project Metadata
        AppendProjectMetadata(sb, context);

        // Section 3 — Build Error
        AppendBuildError(sb, context.Error);

        // Section 4 — Source File (may be trimmed)
        var sourceSection = BuildSourceFileSection(context.Error, context.SourceFileContent);

        // Section 5 — Available API Surface (may be trimmed)
        var apiSurfaceSection = BuildApiSurfaceSection(context.RelatedTypes);

        // Section 6 — Output Format Constraint + Negative Examples
        var instructionsSection = BuildInstructionsSection(context.NegativeExamples, context.Error.ErrorCode);

        // Apply token budget trimming
        var currentEstimate = EstimateTokens(sb.ToString()) 
            + EstimateTokens(sourceSection) 
            + EstimateTokens(apiSurfaceSection) 
            + EstimateTokens(instructionsSection);

        if (currentEstimate > _tokenBudget)
        {
            // Trimming strategy:
            // 1. Remove least-relevant type summaries
            // 2. Truncate source file to ±50 lines around error
            // 3. Summarize packages (top 10 only)

            var trimmedTypes = TrimTypeSummaries(context.RelatedTypes, context.Error);
            apiSurfaceSection = BuildApiSurfaceSection(trimmedTypes);

            currentEstimate = EstimateTokens(sb.ToString()) 
                + EstimateTokens(sourceSection) 
                + EstimateTokens(apiSurfaceSection) 
                + EstimateTokens(instructionsSection);

            if (currentEstimate > _tokenBudget)
            {
                sourceSection = BuildTruncatedSourceSection(context.Error, context.SourceFileContent, 50);

                currentEstimate = EstimateTokens(sb.ToString()) 
                    + EstimateTokens(sourceSection) 
                    + EstimateTokens(apiSurfaceSection) 
                    + EstimateTokens(instructionsSection);

                if (currentEstimate > _tokenBudget)
                {
                    // Last resort: drop API surface entirely
                    apiSurfaceSection = "## Available API Surface\n(Omitted due to token budget — rely on the source file and error message.)\n";
                }
            }
        }

        sb.Append(sourceSection);
        sb.Append(apiSurfaceSection);
        sb.Append(instructionsSection);

        return sb.ToString();
    }

    private void AppendSystemRole(StringBuilder sb)
    {
        sb.AppendLine("""
            You are a senior C# / .NET developer. You fix build errors in a Blazor application
            that uses .NET Aspire for orchestration. You MUST only use types, methods, and
            properties that are explicitly listed in the "Available API Surface" section below.
            Do NOT invent or hallucinate any APIs.
            """);
        sb.AppendLine();
    }

    private void AppendProjectMetadata(StringBuilder sb, BuildErrorContext context)
    {
        sb.AppendLine("## Project Metadata");
        sb.AppendLine($"- Solution: Blazor-Data-Orchestrator");
        sb.AppendLine($"- Project: {context.ProjectName}");
        sb.AppendLine($"- Target Framework: {context.TargetFramework}");

        if (!string.IsNullOrEmpty(context.AspireVersion))
            sb.AppendLine($"- Aspire version: {context.AspireVersion}");

        if (context.Packages.Count > 0)
        {
            sb.AppendLine("- Key NuGet packages:");
            var packagesToShow = context.Packages.Count > 10 
                ? context.Packages.Take(10).ToList() 
                : context.Packages;
            foreach (var pkg in packagesToShow)
            {
                sb.AppendLine($"  - {pkg.Id} {pkg.Version}");
            }
            if (context.Packages.Count > 10)
                sb.AppendLine($"  - ... and {context.Packages.Count - 10} more");
        }
        sb.AppendLine();
    }

    private void AppendBuildError(StringBuilder sb, BuildError error)
    {
        sb.AppendLine("## Build Error");
        sb.AppendLine($"- Code: {error.ErrorCode}");
        sb.AppendLine($"- Message: {error.Message}");
        sb.AppendLine($"- File: {error.FilePath}");
        sb.AppendLine($"- Line: {error.Line}, Column: {error.Column}");
        sb.AppendLine();
    }

    private string BuildSourceFileSection(BuildError error, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "## Source File\n(Source file content unavailable.)\n\n";

        var sb = new StringBuilder();
        sb.AppendLine($"## Source File ({error.FilePath})");
        sb.AppendLine("```csharp");

        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            sb.AppendLine($"{i + 1,5}: {lines[i].TrimEnd('\r')}");
        }

        sb.AppendLine("```");
        sb.AppendLine();
        return sb.ToString();
    }

    private string BuildTruncatedSourceSection(BuildError error, string content, int windowSize)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "## Source File\n(Source file content unavailable.)\n\n";

        var lines = content.Split('\n');
        var startLine = Math.Max(0, error.Line - 1 - windowSize);
        var endLine = Math.Min(lines.Length - 1, error.Line - 1 + windowSize);

        var sb = new StringBuilder();
        sb.AppendLine($"## Source File ({error.FilePath}) — truncated to ±{windowSize} lines around error");
        sb.AppendLine("```csharp");

        if (startLine > 0)
            sb.AppendLine($"    // ... lines 1–{startLine} omitted ...");

        for (int i = startLine; i <= endLine; i++)
        {
            var marker = (i == error.Line - 1) ? " >>> " : "     ";
            sb.AppendLine($"{i + 1,5}:{marker}{lines[i].TrimEnd('\r')}");
        }

        if (endLine < lines.Length - 1)
            sb.AppendLine($"    // ... lines {endLine + 2}–{lines.Length} omitted ...");

        sb.AppendLine("```");
        sb.AppendLine();
        return sb.ToString();
    }

    private string BuildApiSurfaceSection(List<TypeSummary> types)
    {
        if (types.Count == 0)
            return "## Available API Surface\n(No type summaries available.)\n\n";

        var sb = new StringBuilder();
        sb.AppendLine("## Available API Surface");
        sb.AppendLine("The following type summaries are the ONLY APIs you may reference.");
        sb.AppendLine();

        foreach (var type in types)
        {
            sb.AppendLine($"### {type.FullName}");
            sb.AppendLine("```csharp");
            sb.AppendLine(type.PublicMemberSummary);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string BuildInstructionsSection(List<NegativeExample> negativeExamples, string errorCode)
    {
        var sb = new StringBuilder();

        // Negative examples (if any)
        if (negativeExamples.Count > 0)
        {
            sb.AppendLine($"## Common Mistakes to Avoid for {errorCode}");
            foreach (var ex in negativeExamples)
            {
                sb.AppendLine($"- Do NOT use `{ex.BadSymbol}`; it does not exist. Use `{ex.CorrectAlternative}` instead.");
            }
            sb.AppendLine();
        }

        // Output format constraint
        sb.AppendLine("## Instructions");
        sb.AppendLine("1. Identify the root cause of the build error.");
        sb.AppendLine("2. Produce a SINGLE corrected version of the file.");
        sb.AppendLine("3. Output ONLY a fenced C# code block containing the entire corrected file.");
        sb.AppendLine("4. Do NOT add explanatory text outside the code block.");
        sb.AppendLine("5. Do NOT introduce new NuGet packages.");
        sb.AppendLine("6. Preserve all existing `using` directives unless one is the cause of the error.");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Trims type summaries by keeping only the types closest to the error symbol.
    /// </summary>
    private List<TypeSummary> TrimTypeSummaries(List<TypeSummary> types, BuildError error)
    {
        if (types.Count <= 3) return types;

        // Extract identifier from error message to rank relevance
        var errorIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identifierPattern = new System.Text.RegularExpressions.Regex(@"'(\w+)'");
        foreach (System.Text.RegularExpressions.Match m in identifierPattern.Matches(error.Message))
        {
            errorIdentifiers.Add(m.Groups[1].Value);
        }

        // Rank types: those whose name appears in the error message are most relevant
        var ranked = types
            .OrderByDescending(t => errorIdentifiers.Any(id => t.FullName.Contains(id, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
            .ThenBy(t => t.PublicMemberSummary.Length) // Prefer smaller summaries
            .Take(5)
            .ToList();

        return ranked;
    }

    private int EstimateTokens(string text)
    {
        return text.Length / CharsPerToken;
    }
}
