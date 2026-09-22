namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Thrown for AI configuration problems detected before any network call,
/// such as a malformed endpoint or a non-https URL.
/// </summary>
public sealed class AIConfigurationException : Exception
{
    public AIConfigurationException(string message) : base(message)
    {
    }
}
