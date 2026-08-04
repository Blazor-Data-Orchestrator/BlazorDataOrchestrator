using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using Azure.AI.OpenAI;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Builds an <see cref="IChatClient"/> for a given <see cref="AISettings"/> configuration.
/// Modeled on the SimpleChat ChatClientFactory pattern, this isolates per-provider
/// construction so each provider's setup is testable and centralized.
/// </summary>
public static class ChatClientFactory
{
    /// <summary>
    /// Creates an <see cref="IChatClient"/> for the configured provider, or
    /// <c>null</c> when the settings are not configured.
    /// </summary>
    public static IChatClient? Create(AISettings settings)
    {
        if (!settings.IsConfigured)
        {
            return null;
        }

        return settings.AIServiceType switch
        {
            "Azure OpenAI" => CreateAzureOpenAI(settings),
            "Anthropic" => new AnthropicChatClientAdapter(settings.ApiKey, settings.AIModel),
            "Google AI" => new GoogleAIChatClientAdapter(settings.ApiKey, settings.AIModel),
            _ => CreateOpenAI(settings),
        };
    }

    private static IChatClient CreateAzureOpenAI(AISettings settings)
    {
        // AI Foundry endpoints use OpenAI-compatible /v1 path and don't need api-version.
        if (IsAIFoundryEndpoint(settings.Endpoint))
        {
            var endpoint = ResolveAzureEndpoint(settings);
            var openAIOptions = new OpenAIClientOptions { Endpoint = endpoint };
            var client = new OpenAIClient(new ApiKeyCredential(settings.ApiKey), openAIOptions);
            return client.GetChatClient(settings.AIModel).AsIChatClient();
        }

        // Traditional Azure OpenAI: apply the configured API version when supplied.
        var options = BuildAzureOptions(settings);
        var azureEndpoint = ResolveAzureEndpoint(settings);

        var azureClient = new AzureOpenAIClient(
            azureEndpoint,
            new ApiKeyCredential(settings.ApiKey),
            options);

        return azureClient.GetChatClient(settings.AIModel).AsIChatClient();
    }

    /// <summary>
    /// Resolves the Azure OpenAI endpoint, combining a custom deployment path when present.
    /// </summary>
    public static Uri ResolveAzureEndpoint(AISettings settings)
    {
        var baseEndpoint = settings.Endpoint.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(settings.DeploymentPath))
        {
            return new Uri(baseEndpoint);
        }

        var path = settings.DeploymentPath.Trim();

        // Allow a full absolute URL or a relative path appended to the endpoint.
        return Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri($"{baseEndpoint}/{path.TrimStart('/')}");
    }

    /// <summary>
    /// Builds <see cref="AzureOpenAIClientOptions"/>, applying the configured API version
    /// when it maps to a known <see cref="AzureOpenAIClientOptions.ServiceVersion"/>.
    /// </summary>
    private static AzureOpenAIClientOptions BuildAzureOptions(AISettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiVersion)
            && TryParseServiceVersion(settings.ApiVersion, out var version))
        {
            return new AzureOpenAIClientOptions(version);
        }

        return new AzureOpenAIClientOptions();
    }

    /// <summary>
    /// Maps an API version string (e.g. "2024-10-21" or "2024-08-01-preview") to the
    /// matching <see cref="AzureOpenAIClientOptions.ServiceVersion"/> enum value.
    /// </summary>
    private static bool TryParseServiceVersion(string apiVersion, out AzureOpenAIClientOptions.ServiceVersion version)
    {
        // "2024-10-21" -> "V2024_10_21"; "2024-08-01-preview" -> "V2024_08_01_preview".
        // Enum.TryParse with ignoreCase matches the "_Preview" suffix casing.
        var normalized = "V" + apiVersion.Trim().Replace("-", "_");
        return Enum.TryParse(normalized, ignoreCase: true, out version);
    }

    /// <summary>
    /// Detects Azure AI Foundry endpoints that use path-based versioning (/v1) instead of ?api-version=.
    /// </summary>
    public static bool IsAIFoundryEndpoint(string endpoint)
    {
        return !string.IsNullOrWhiteSpace(endpoint)
            && endpoint.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
    }

    private static IChatClient CreateOpenAI(AISettings settings)
    {
        var openAIClient = new OpenAIClient(new ApiKeyCredential(settings.ApiKey));
        return openAIClient.GetChatClient(settings.AIModel).AsIChatClient();
    }
}
