using BlazorDataOrchestrator.Core.Models;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Decides whether the API key held in the AI settings editor may be sent to the endpoint currently
/// typed in the editor. A stored key is only sent to the endpoint it was saved with, so editing the
/// endpoint cannot redirect the organisation's key to another server (SEC-003).
/// </summary>
public sealed class AIKeyEndpointGuard
{
    private readonly HashSet<(string Key, string Endpoint)> _savedPairs = new();
    private readonly HashSet<string> _userEnteredKeys = new(StringComparer.Ordinal);

    public void TrustSaved(AIProviderSettings provider)
    {
        if (!string.IsNullOrEmpty(provider.ApiKey))
        {
            _savedPairs.Add((provider.ApiKey, Normalize(provider.Endpoint)));
        }
    }

    public void TrustUserEnteredKey(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            _userEnteredKeys.Add(key);
        }
    }

    public bool CanSendKey(AIServiceType serviceType, AIProviderSettings provider)
    {
        // Only these providers take a user-supplied endpoint; the others call fixed vendor hosts.
        if (serviceType is not (AIServiceType.AzureOpenAI or AIServiceType.AzureAIFoundry))
        {
            return true;
        }

        if (string.IsNullOrEmpty(provider.ApiKey) || _userEnteredKeys.Contains(provider.ApiKey))
        {
            return true;
        }

        return _savedPairs.Contains((provider.ApiKey, Normalize(provider.Endpoint)));
    }

    private static string Normalize(string? endpoint) =>
        (endpoint ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
}
