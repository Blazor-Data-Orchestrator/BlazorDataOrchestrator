using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using Azure.AI.OpenAI;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services.Foundry;

namespace BlazorDataOrchestrator.Core.Services;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

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
    public static IChatClient? Create(AISettings settings) => Create(settings.Active);

    /// <summary>
    /// Creates an <see cref="IChatClient"/> for a single provider's settings, or
    /// <c>null</c> when the settings are not configured.
    /// </summary>
    public static IChatClient? Create(AIProviderSettings settings)
    {
        if (!settings.IsConfigured)
        {
            return null;
        }

        return settings.ServiceType switch
        {
            ServiceKind.AzureAIFoundry => CreateFoundry(settings),
            ServiceKind.AzureOpenAI => CreateAzureOpenAI(settings),
            ServiceKind.Anthropic => new AnthropicChatClientAdapter(settings.ApiKey, settings.AIModel),
            ServiceKind.GoogleAI => new GoogleAIChatClientAdapter(settings.ApiKey, settings.AIModel),
            _ => CreateOpenAI(settings),
        };
    }

    /// <summary>
    /// Resolves the endpoint and returns the client for the resulting wire protocol.
    /// </summary>
    /// <exception cref="AIConfigurationException">The endpoint could not be resolved.</exception>
    public static IChatClient CreateFoundry(AIProviderSettings settings)
    {
        var resolution = ResolveFoundry(settings);

        return resolution.Protocol switch
        {
            FoundryApiProtocol.AnthropicMessages =>
                new AnthropicChatClientAdapter(settings.ApiKey.Trim(), settings.AIModel, resolution.BaseAddress),

            FoundryApiProtocol.Responses =>
                CreateResponsesClient(settings, resolution.BaseAddress),

            _ =>
                CreateOpenAICompatibleClient(settings.ApiKey, resolution.BaseAddress)
                    .GetChatClient(settings.AIModel)
                    .AsIChatClient(),
        };
    }

    /// <summary>
    /// Resolves the request target for a provider, or throws when the endpoint is unusable.
    /// </summary>
    /// <exception cref="AIConfigurationException">The endpoint could not be resolved.</exception>
    public static FoundryEndpointResolution ResolveFoundry(AIProviderSettings settings)
    {
        if (!FoundryEndpointResolver.TryResolve(
                settings.Endpoint, settings.AIModel, settings.ParsedApiProtocol, out var resolution, out var error))
        {
            throw new AIConfigurationException(error!);
        }

        return resolution!;
    }

#pragma warning disable OPENAI001 // The Responses API is still marked experimental.
    private static IChatClient CreateResponsesClient(AIProviderSettings settings, Uri baseAddress)
        => CreateOpenAICompatibleClient(settings.ApiKey, baseAddress)
            .GetResponsesClient()
            .AsIChatClient(settings.AIModel);
#pragma warning restore OPENAI001

    private static OpenAIClient CreateOpenAICompatibleClient(string apiKey, Uri baseAddress)
        => new(new ApiKeyCredential(apiKey.Trim()), new OpenAIClientOptions { Endpoint = baseAddress });

    private static IChatClient CreateAzureOpenAI(AIProviderSettings settings)
    {
        // Foundry hosts, /openai/v1, /anthropic, and project URLs can't use the classic deployments route.
        if (FoundryEndpointResolver.ShouldUseV1Routing(settings.Endpoint))
        {
            return CreateFoundry(settings);
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
    public static Uri ResolveAzureEndpoint(AIProviderSettings settings)
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
    private static AzureOpenAIClientOptions BuildAzureOptions(AIProviderSettings settings)
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
    /// Detects endpoints that must use the Foundry v1 surfaces instead of ?api-version= routing.
    /// </summary>
    public static bool IsAIFoundryEndpoint(string endpoint)
        => FoundryEndpointResolver.ShouldUseV1Routing(endpoint);

    private static IChatClient CreateOpenAI(AIProviderSettings settings)
    {
        var openAIClient = new OpenAIClient(new ApiKeyCredential(settings.ApiKey));
        return openAIClient.GetChatClient(settings.AIModel).AsIChatClient();
    }
}
