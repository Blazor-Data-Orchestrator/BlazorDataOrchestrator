using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Per-model request feature support. Sending an unsupported option makes the
/// service answer 400, so these options are omitted up front.
/// </summary>
public static class ModelCapabilities
{
    /// <summary>
    /// Reasoning models and Claude deployments with adaptive thinking reject a custom temperature.
    /// </summary>
    public static bool SupportsTemperature(string? model, FoundryApiProtocol protocol)
    {
        if (protocol == FoundryApiProtocol.AnthropicMessages)
        {
            return false;
        }

        var name = Normalize(model);

        if (name.Length == 0)
        {
            return true;
        }

        if (name.StartsWith("o1") || name.StartsWith("o3") || name.StartsWith("o4") || name.StartsWith("gpt-5") || name.StartsWith("gpt5"))
        {
            return false;
        }

        return !name.Contains("codex") && !name.StartsWith("claude");
    }

    /// <summary>
    /// Tool calling support. The Anthropic adapter doesn't map tools, and some
    /// Foundry partner models reject a <c>tools</c> array outright.
    /// </summary>
    public static bool SupportsTools(string? model, FoundryApiProtocol protocol)
    {
        if (protocol == FoundryApiProtocol.AnthropicMessages)
        {
            return false;
        }

        var name = Normalize(model);

        return !name.Contains("deepseek-r1") && !name.StartsWith("claude");
    }

    private static string Normalize(string? model) => (model ?? "").Trim().ToLowerInvariant();
}
