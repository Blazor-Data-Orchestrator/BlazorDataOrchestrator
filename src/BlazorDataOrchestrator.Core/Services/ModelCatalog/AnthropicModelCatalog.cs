using System.Text.Json;
using BlazorDataOrchestrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Lists models from the Anthropic /v1/models endpoint, following pagination.
/// </summary>
public sealed class AnthropicModelCatalog : HttpModelCatalogBase
{
    private const string ApiVersion = "2023-06-01";
    private const int MaxPages = 10;

    public AnthropicModelCatalog(IHttpClientFactory httpClientFactory, ILogger<AnthropicModelCatalog> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override ServiceKind ServiceType => ServiceKind.Anthropic;

    public override async Task<ModelListResult> ListModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return ModelListResult.NotConfigured("Enter an API key to load models.");
        }

        var models = new List<string>();
        string? afterId = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = "https://api.anthropic.com/v1/models?limit=100";
            if (afterId is not null)
            {
                url += $"&after_id={Uri.EscapeDataString(afterId)}";
            }

            string? body = null;

            var failure = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("x-api-key", settings.ApiKey.Trim());
                request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
                return request;
            }, cancellationToken, content => body = content);

            if (failure is not null)
            {
                return failure;
            }

            using var doc = JsonDocument.Parse(body!);

            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var model in data.EnumerateArray())
                {
                    if (model.TryGetProperty("id", out var id) && id.GetString() is { } value)
                    {
                        models.Add(value);
                    }
                }
            }

            var hasMore = doc.RootElement.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
            afterId = doc.RootElement.TryGetProperty("last_id", out var lastId) ? lastId.GetString() : null;

            if (!hasMore || string.IsNullOrEmpty(afterId))
            {
                break;
            }
        }

        return ModelListResult.Success(models.Distinct().OrderByDescending(m => m, StringComparer.Ordinal).ToList());
    }
}
