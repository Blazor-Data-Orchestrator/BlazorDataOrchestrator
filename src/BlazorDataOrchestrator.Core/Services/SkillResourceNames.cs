namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Logical names under which host projects embed the <c>.github/skills/*/SKILL.md</c> files.
/// </summary>
public static class SkillResourceNames
{
    public const string CSharp = "Skills/coding-a-job-csharp/SKILL.md";
    public const string Python = "Skills/coding-a-job-python/SKILL.md";

    public static IReadOnlyList<string> All { get; } = [CSharp, Python];

    public static string ForLanguage(string? language) =>
        language?.Trim().ToLowerInvariant() switch
        {
            "python" or "py" => Python,
            _ => CSharp
        };

    /// <summary>Returns the skill name, e.g. <c>coding-a-job-csharp</c>, for a logical resource name.</summary>
    public static string GetSkillName(string resourceName)
    {
        var parts = resourceName.Split('/');
        return parts.Length >= 2 ? parts[^2] : resourceName;
    }
}
