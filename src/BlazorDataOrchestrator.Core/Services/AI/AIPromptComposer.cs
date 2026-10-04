using System.Text;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services.AI;

/// <summary>
/// Builds the AI system prompt shared by the web Code Assistant and the JobCreatorTemplate chat:
/// base rules, file targeting rules, then the project SKILL.md for the job language.
/// </summary>
public static class AIPromptComposer
{
    public const string SkillBeginMarker = "<!-- BEGIN SKILL -->";
    public const string SkillEndMarker = "<!-- END SKILL -->";

    public const string FileTargetingRules = @"## File Targeting Rules (always apply)
- The user message contains an Editor Context listing the job files, the ACTIVE file (open in the editor), and the PRIMARY code file.
- Code changes go to the PRIMARY code file, even when a .json file is ACTIVE.
- Never put source code inside .json, .nuspec, or .txt files.
- Every .json file you output must be strictly valid JSON: one root object, double-quoted names and strings, no comments, no trailing commas.
- ###UPDATED CODE BEGIN### / ###UPDATED CODE END### always targets the PRIMARY code file.
- For any other file use ###UPDATED FILE BEGIN: <exact file name>### / ###UPDATED FILE END###.
- Return complete file contents, and only for files you changed.";

    public static string Compose(string basePrompt, IInstructionsProvider? instructionsProvider, string language)
    {
        var sb = new StringBuilder();
        sb.AppendLine(basePrompt.TrimEnd());
        sb.AppendLine();
        sb.AppendLine(FileTargetingRules);

        if (instructionsProvider is null)
        {
            return sb.ToString();
        }

        var skill = instructionsProvider.GetInstructionsForLanguage(language);
        if (string.IsNullOrWhiteSpace(skill))
        {
            return sb.ToString();
        }

        var info = instructionsProvider.GetInfo(language);
        sb.AppendLine();
        sb.AppendLine($"## Project Skill: {SkillResourceNames.GetSkillName(info.ResourceName)}");
        sb.AppendLine("The following skill is authoritative. Follow it exactly.");
        sb.AppendLine(SkillBeginMarker);
        sb.AppendLine(skill.Trim());
        sb.AppendLine(SkillEndMarker);
        return sb.ToString();
    }

    public static string Compose(string basePrompt, IInstructionsProvider? instructionsProvider, AIEditorContext context) =>
        Compose(basePrompt, instructionsProvider, context.Language);
}
