using System.Text.Json;
using BlazorDataOrchestrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Lists Azure OpenAI deployment names. Deployment names, not base model ids,
/// are what the chat client needs.
/// </summary>
public sealed class AzureOpenAIModelCatalog : HttpModelCatalogBase
{
    public AzureOpenAIModelCatalog(IHttpClientFactory httpClientFactory, ILogger<AzureOpenAIModelCatalog> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override ServiceKind ServiceType => ServiceKind.AzureOpenAI;

    // The deployments route is only served by the older data-plane api-versions, so newer
    // versions (e.g. 2024-12-01-preview) answer 404. Try the known routes in order.
    private const string DeploymentsApiVersion = "2023-03-15-preview";
    private const string ModelsApiVersion = "2024-06-01";
    private const string AnthropicVersion = "2023-06-01";

    public override async Task<ModelListResult> ListModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return ModelListResult.NotConfigured("Enter an API key to load deployments.");
        }

        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return ModelListResult.NotConfigured("Enter the Azure OpenAI endpoint to load deployments.");
        }

        var isFoundry = ChatClientFactory.IsAIFoundryEndpoint(settings.Endpoint);
        var baseEndpoint = settings.Endpoint.TrimEnd('/');
        var isAnthropicPassthrough = baseEndpoint.Contains("/anthropic", StringComparison.OrdinalIgnoreCase);
        var apiKey = settings.ApiKey.Trim();

        ModelListResult? lastFailure = null;

        foreach (var url in BuildCandidateUrls(baseEndpoint, isFoundry, isAnthropicPassthrough, settings.ApiVersion))
        {
            string? body = null;

            var failure = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (isAnthropicPassthrough)
                {
                    // Foundry's Anthropic passthrough expects Anthropic-style auth, not Bearer.
                    request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                    request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
                    request.Headers.TryAddWithoutValidation("api-key", apiKey);
                }
                else if (isFoundry)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                    request.Headers.TryAddWithoutValidation("api-key", apiKey);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation("api-key", apiKey);
                }
                return request;
            }, cancellationToken, content => body = content);

            if (failure is not null)
            {
                if (failure.Status == ModelListStatus.InvalidKey)
                {
                    return failure;
                }

                lastFailure = failure;
                continue;
            }

            var models = ParseModels(body!);
            if (models.Count > 0)
            {
                return ModelListResult.Success(models);
            }

            lastFailure = ModelListResult.Empty();
        }

        return lastFailure ?? ModelListResult.Empty();
    }

    private static IEnumerable<string> BuildCandidateUrls(string baseEndpoint, bool isFoundry, bool isAnthropicPassthrough, string apiVersion)
    {
        if (isAnthropicPassthrough)
        {
            yield return $"{baseEndpoint}/models?limit=100";
            yield break;
        }

        if (isFoundry)
        {
            yield return $"{baseEndpoint}/deployments";
            yield return $"{baseEndpoint}/models";
            yield break;
        }

        // Deployment names are what the chat client needs, so try those routes first.
        yield return $"{baseEndpoint}/openai/deployments?api-version={DeploymentsApiVersion}";

        var configured = apiVersion?.Trim();
        if (!string.IsNullOrEmpty(configured) && configured != DeploymentsApiVersion)
        {
            yield return $"{baseEndpoint}/openai/deployments?api-version={configured}";
        }

        yield return $"{baseEndpoint}/openai/v1/models";
        yield return $"{baseEndpoint}/openai/models?api-version={(string.IsNullOrEmpty(configured) ? ModelsApiVersion : configured)}";
    }

    private static List<string> ParseModels(string body)
    {
        using var doc = JsonDocument.Parse(body);

        var deployments = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var deployment in data.EnumerateArray())
            {
                // The deployments list uses "id"; some surfaces only return "name".
                var value = deployment.TryGetProperty("id", out var id) ? id.GetString() : null;
                value ??= deployment.TryGetProperty("name", out var name) ? name.GetString() : null;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    deployments.Add(value!);
                }
            }
        }

        return deployments.Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList();
    }
}
