namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Provides language-specific instructions for AI code assistance.
/// </summary>
public interface IInstructionsProvider
{
    /// <summary>
    /// Gets the instructions for the specified programming language.
    /// </summary>
    string GetInstructionsForLanguage(string language);

    /// <summary>
    /// Returns metadata about the loaded instructions (resource name, length, SHA-256 prefix) for logging and diagnostics.
    /// </summary>
    SkillInstructionsInfo GetInfo(string language);
}

/// <summary>
/// Describes a loaded AI skill resource.
/// </summary>
public sealed record SkillInstructionsInfo(string ResourceName, bool Found, int Length, string Sha256);
