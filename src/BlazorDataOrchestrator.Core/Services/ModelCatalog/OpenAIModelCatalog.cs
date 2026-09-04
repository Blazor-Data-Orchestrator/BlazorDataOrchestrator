using System.Net.Http.Headers;
using System.Text.Json;
using BlazorDataOrchestrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Lists chat-capable models from the OpenAI /v1/models endpoint.
/// </summary>
public sealed class OpenAIModelCatalog : HttpModelCatalogBase
{
    public OpenAIModelCatalog(IHttpClientFactory httpClientFactory, ILogger<OpenAIModelCatalog> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override ServiceKind ServiceType => ServiceKind.OpenAI;

    public override async Task<ModelListResult> ListModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return ModelListResult.NotConfigured("Enter an API key to load models.");
        }

        string? body = null;

        var failure = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
            return request;
        }, cancellationToken, content => body = content);

        if (failure is not null)
        {
            return failure;
        }

        using var doc = JsonDocument.Parse(body!);

        var models = new List<string>();
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

        var filtered = models
            .Where(m => m.StartsWith("gpt-") || m.StartsWith("chatgpt-") || m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4"))
            .Where(m => !m.Contains("instruct") && !m.Contains("realtime") && !m.Contains("audio") && !m.Contains("transcribe") && !m.Contains("tts"))
            .Distinct()
            .OrderByDescending(m => m, StringComparer.Ordinal)
            .ToList();

        return ModelListResult.Success(filtered);
    }
}
