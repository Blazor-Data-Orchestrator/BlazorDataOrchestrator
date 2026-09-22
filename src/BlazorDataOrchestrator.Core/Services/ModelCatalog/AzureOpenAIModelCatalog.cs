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

        foreach (var candidate in BuildCandidateUrls(baseEndpoint, isFoundry, isAnthropicPassthrough, settings.ApiVersion))
        {
            string? body = null;

            var failure = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, candidate.Url);
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
                if (candidate.Anthropic)
                {
                    // Foundry's Anthropic passthrough expects Anthropic-style auth, not Bearer.
                    request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                    request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
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

    private readonly record struct Candidate(string Url, bool Anthropic);

    private static IEnumerable<Candidate> BuildCandidateUrls(string baseEndpoint, bool isFoundry, bool isAnthropicPassthrough, string apiVersion)
    {
        var configured = apiVersion?.Trim();

        if (isAnthropicPassthrough)
        {
            yield return new Candidate($"{baseEndpoint}/models?limit=100", true);

            // The Anthropic passthrough has no listing route on most resources, so fall
            // back to the Foundry account root, which lists every deployment.
            var root = GetAccountRoot(baseEndpoint);
            if (root is not null)
            {
                yield return new Candidate($"{root}/openai/v1/models", false);
                yield return new Candidate($"{root}/models?api-version={(string.IsNullOrEmpty(configured) ? ModelsApiVersion : configured)}", false);
                yield return new Candidate($"{root}/openai/deployments?api-version={DeploymentsApiVersion}", false);
            }

            yield break;
        }

        if (isFoundry)
        {
            yield return new Candidate($"{baseEndpoint}/deployments", false);
            yield return new Candidate($"{baseEndpoint}/models", false);

            var root = GetAccountRoot(baseEndpoint);
            if (root is not null)
            {
                yield return new Candidate($"{root}/openai/deployments?api-version={DeploymentsApiVersion}", false);
            }

            yield break;
        }

        // Deployment names are what the chat client needs, so try those routes first.
        yield return new Candidate($"{baseEndpoint}/openai/deployments?api-version={DeploymentsApiVersion}", false);

        if (!string.IsNullOrEmpty(configured) && configured != DeploymentsApiVersion)
        {
            yield return new Candidate($"{baseEndpoint}/openai/deployments?api-version={configured}", false);
        }

        yield return new Candidate($"{baseEndpoint}/openai/v1/models", false);
        yield return new Candidate($"{baseEndpoint}/openai/models?api-version={(string.IsNullOrEmpty(configured) ? ModelsApiVersion : configured)}", false);
    }

    private static string? GetAccountRoot(string baseEndpoint)
    {
        if (!Uri.TryCreate(baseEndpoint, UriKind.Absolute, out var uri) || uri.AbsolutePath.Length <= 1)
        {
            return null;
        }

        return $"{uri.Scheme}://{uri.Authority}";
    }

    private static List<string> ParseModels(string body) => ModelListParser.ParseDeployments(body);
}
