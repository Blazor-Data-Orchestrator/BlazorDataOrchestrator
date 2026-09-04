using System.Text.Json;
using BlazorDataOrchestrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Lists Google AI models that support generateContent.
/// </summary>
public sealed class GoogleAIModelCatalog : HttpModelCatalogBase
{
    public GoogleAIModelCatalog(IHttpClientFactory httpClientFactory, ILogger<GoogleAIModelCatalog> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override ServiceKind ServiceType => ServiceKind.GoogleAI;

    public override async Task<ModelListResult> ListModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return ModelListResult.NotConfigured("Enter an API key to load models.");
        }

        string? body = null;

        var failure = await SendAsync(() => new HttpRequestMessage(
            HttpMethod.Get,
            $"https://generativelanguage.googleapis.com/v1beta/models?pageSize=200&key={Uri.EscapeDataString(settings.ApiKey.Trim())}"),
            cancellationToken,
            content => body = content);

        if (failure is not null)
        {
            return failure;
        }

        using var doc = JsonDocument.Parse(body!);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var data))
        {
            foreach (var model in data.EnumerateArray())
            {
                if (!model.TryGetProperty("name", out var name) || name.GetString() is not { } fullName)
                {
                    continue;
                }

                var supportsGenerate = model.TryGetProperty("supportedGenerationMethods", out var methods)
                    && methods.EnumerateArray().Any(m => m.GetString() == "generateContent");

                if (!supportsGenerate)
                {
                    continue;
                }

                var id = fullName.StartsWith("models/") ? fullName["models/".Length..] : fullName;

                if (!id.Contains("embedding") && !id.Contains("aqa") && !id.Contains("imagen"))
                {
                    models.Add(id);
                }
            }
        }

        return ModelListResult.Success(models.Distinct().OrderByDescending(m => m, StringComparer.Ordinal).ToList());
    }
}
