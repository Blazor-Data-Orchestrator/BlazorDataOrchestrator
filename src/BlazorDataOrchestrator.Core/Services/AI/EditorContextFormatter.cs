using System.Text;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services.AI;

/// <summary>
/// Renders an <see cref="AIEditorContext"/> as labeled markdown so the AI can tell the ACTIVE
/// file (open in the editor) apart from the PRIMARY code file it should normally change.
/// </summary>
public static class EditorContextFormatter
{
    /// <summary>Files larger than this are listed but not included, to keep the prompt bounded.</summary>
    public const int MaxOtherFileChars = 20_000;

    private static bool IsSameFile(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static string Format(AIEditorContext context, string userRequest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Editor Context");
        sb.AppendLine("Job files:");

        IEnumerable<string> files = context.AvailableFiles.Count > 0
            ? context.AvailableFiles
            : new[] { context.PrimaryCodeFileName };
        if (!files.Contains(context.PrimaryCodeFileName, StringComparer.OrdinalIgnoreCase))
        {
            files = files.Prepend(context.PrimaryCodeFileName);
        }

        foreach (var file in files)
        {
            sb.Append("- ").Append(file);
            var roles = GetRoles(context, file);
            if (roles.Count > 0)
            {
                sb.Append("  (").Append(string.Join(", ", roles)).Append(')');
            }
            sb.AppendLine();
        }

        var nuspecIsActive = !string.IsNullOrEmpty(context.NuspecFileName) &&
            string.Equals(context.NuspecFileName, context.ActiveFileName, StringComparison.OrdinalIgnoreCase);

        if (context.ActiveIsPrimary)
        {
            AppendFileBlock(sb, "PRIMARY code file, ACTIVE in editor", context.PrimaryCodeFileName, context.ActiveFileContent);
        }
        else
        {
            AppendFileBlock(sb, "ACTIVE file", context.ActiveFileName, context.ActiveFileContent);
            AppendFileBlock(sb, "PRIMARY code file", context.PrimaryCodeFileName, context.PrimaryCodeContent);
            sb.AppendLine($"Note: the ACTIVE file is not the PRIMARY code file. Put code changes in {context.PrimaryCodeFileName}, not in {context.ActiveFileName}.");
            sb.AppendLine();
        }

        if (!nuspecIsActive && !string.IsNullOrWhiteSpace(context.NuspecContent) && !string.IsNullOrEmpty(context.NuspecFileName))
        {
            AppendFileBlock(sb, "NuGet manifest", context.NuspecFileName, context.NuspecContent);
        }

        var otherFiles = context.OtherFileContents
            .Where(p => !IsSameFile(p.Key, context.ActiveFileName) &&
                        !IsSameFile(p.Key, context.PrimaryCodeFileName) &&
                        !IsSameFile(p.Key, context.NuspecFileName))
            .ToList();
        if (otherFiles.Count > 0)
        {
            sb.AppendLine("When you change any of the following files, start from the current content shown here and keep every existing setting unless the user asked to remove it.");
            foreach (var (fileName, content) in otherFiles)
            {
                if (content.Length > MaxOtherFileChars)
                {
                    sb.AppendLine();
                    sb.AppendLine($"### Other job file: {fileName} ({content.Length} characters, too large to include — do not modify it)");
                    sb.AppendLine();
                    continue;
                }
                AppendFileBlock(sb, "Other job file", fileName, content);
            }
        }

        sb.AppendLine("## User Request:");
        sb.Append(userRequest);
        return sb.ToString();
    }

    private static List<string> GetRoles(AIEditorContext context, string file)
    {
        var roles = new List<string>();
        if (string.Equals(file, context.PrimaryCodeFileName, StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("PRIMARY code file");
        }
        if (string.Equals(file, context.ActiveFileName, StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("ACTIVE in editor");
        }
        return roles;
    }

    private static void AppendFileBlock(StringBuilder sb, string label, string fileName, string content)
    {
        var fenceLanguage = JobFileTypes.GetFenceLanguage(fileName);
        sb.AppendLine();
        sb.AppendLine($"### {label}: {fileName} ({fenceLanguage})");
        sb.AppendLine($"```{fenceLanguage}");
        sb.AppendLine(content ?? string.Empty);
        sb.AppendLine("```");
        sb.AppendLine();
    }
}
