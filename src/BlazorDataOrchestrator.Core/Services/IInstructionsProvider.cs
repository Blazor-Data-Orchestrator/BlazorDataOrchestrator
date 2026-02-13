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
}
