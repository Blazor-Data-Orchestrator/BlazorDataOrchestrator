namespace BlazorDataOrchestrator.Core.Models;

using ServiceKind = AIServiceType;

/// <summary>
/// Conversion helpers so the provider names are defined in exactly one place.
/// </summary>
public static class AIServiceTypes
{
    /// <summary>
    /// Display text shown in the "AI Service Type" dropdown.
    /// </summary>
    public static string ToDisplayName(ServiceKind type) => type switch
    {
        ServiceKind.AzureOpenAI => "Azure OpenAI",
        ServiceKind.Anthropic => "Anthropic",
        ServiceKind.GoogleAI => "Google AI",
        _ => "OpenAI"
    };

    /// <summary>
    /// Stable Table Storage RowKey. Never localise or change these values.
    /// </summary>
    public static string ToStorageKey(ServiceKind type) => type.ToString();

    /// <summary>
    /// Parses a display name, storage key, or legacy string into a service type.
    /// </summary>
    public static bool TryParse(string? value, out ServiceKind result)
    {
        result = ServiceKind.OpenAI;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Replace(" ", "").Replace("-", "").Replace("_", "");

        foreach (var candidate in All)
        {
            if (string.Equals(normalized, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                result = candidate;
                return true;
            }
        }

        // Legacy aliases that don't match the enum names once whitespace is removed.
        switch (normalized.ToLowerInvariant())
        {
            case "azure":
            case "azureopenai":
            case "azureoai":
                result = ServiceKind.AzureOpenAI;
                return true;
            case "google":
            case "gemini":
            case "googlegemini":
                result = ServiceKind.GoogleAI;
                return true;
            case "claude":
                result = ServiceKind.Anthropic;
                return true;
            case "openai":
                result = ServiceKind.OpenAI;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses a value, falling back to <see cref="ServiceKind.OpenAI"/> for unknown input.
    /// </summary>
    public static ServiceKind ParseOrDefault(string? value)
        => TryParse(value, out var result) ? result : ServiceKind.OpenAI;

    public static IReadOnlyList<ServiceKind> All { get; } = new[]
    {
        ServiceKind.OpenAI,
        ServiceKind.AzureOpenAI,
        ServiceKind.Anthropic,
        ServiceKind.GoogleAI
    };
}
