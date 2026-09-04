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

        if (!isFoundry && string.IsNullOrWhiteSpace(settings.ApiVersion))
        {
            return ModelListResult.NotConfigured("Enter the Azure OpenAI API version to load deployments.");
        }

        var url = isFoundry
            ? $"{baseEndpoint}/deployments"
            : $"{baseEndpoint}/openai/deployments?api-version={settings.ApiVersion.Trim()}";

        string? body = null;

        var failure = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (isFoundry)
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {settings.ApiKey.Trim()}");
            }
            else
            {
                request.Headers.TryAddWithoutValidation("api-key", settings.ApiKey.Trim());
            }
            return request;
        }, cancellationToken, content => body = content);

        if (failure is not null)
        {
            return failure;
        }

        using var doc = JsonDocument.Parse(body!);

        var deployments = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var deployment in data.EnumerateArray())
            {
                // The management surface uses "id"; the data-plane list uses "name".
                var value = deployment.TryGetProperty("id", out var id) ? id.GetString() : null;
                value ??= deployment.TryGetProperty("name", out var name) ? name.GetString() : null;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    deployments.Add(value!);
                }
            }
        }

        return ModelListResult.Success(deployments.Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList());
    }
}
