using System.Text.Json;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services.Foundry;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Lists Azure AI Foundry deployment names from the resource account root.
/// A failed listing is not fatal: the editor keeps free-text deployment entry.
/// </summary>
public sealed class AzureAIFoundryModelCatalog : HttpModelCatalogBase
{
    private const string DeploymentsApiVersion = "2023-03-15-preview";

    public AzureAIFoundryModelCatalog(IHttpClientFactory httpClientFactory, ILogger<AzureAIFoundryModelCatalog> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override ServiceKind ServiceType => ServiceKind.AzureAIFoundry;

    public override async Task<ModelListResult> ListModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return ModelListResult.NotConfigured("Enter an API key to load deployments.");
        }

        if (!FoundryEndpointResolver.TryResolve(
                settings.Endpoint, settings.AIModel, settings.ParsedApiProtocol, out var resolution, out var error))
        {
            return ModelListResult.NotConfigured(error!);
        }

        var root = resolution!.AccountRoot.ToString().TrimEnd('/');
        var apiKey = settings.ApiKey.Trim();

        string[] candidates =
        [
            $"{root}/openai/deployments?api-version={DeploymentsApiVersion}",
            $"{root}/openai/v1/models"
        ];

        ModelListResult? lastFailure = null;

        foreach (var candidate in candidates)
        {
            string? body = null;

            var failure = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
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

            var models = ModelListParser.ParseDeployments(body!);
            if (models.Count > 0)
            {
                return ModelListResult.Success(models);
            }

            lastFailure = ModelListResult.Empty();
        }

        return lastFailure ?? ModelListResult.Empty();
    }
}

/// <summary>
/// Shared parsing for the Azure deployment and model listing payloads.
/// </summary>
internal static class ModelListParser
{
    public static List<string> ParseDeployments(string body)
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
