using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services.ModelCatalog;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Fetches AI models from the provider APIs and caches successful results in
/// Azure Table Storage for 24 hours. There are no hard-coded model lists:
/// failures are returned to the caller so the UI can show them.
/// </summary>
public class AIModelCacheService
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly AIProviderRegistry _registry;
    private readonly ILogger<AIModelCacheService> _logger;
    private const string TableName = "AIModelCache";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public AIModelCacheService(
        TableServiceClient tableServiceClient,
        AIProviderRegistry registry,
        ILogger<AIModelCacheService> logger)
    {
        _tableServiceClient = tableServiceClient;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Gets the models available for the given provider settings, using the cache
    /// unless <paramref name="forceRefresh"/> is set.
    /// </summary>
    public async Task<ModelListResult> GetModelsAsync(
        AIProviderSettings settings,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured)
        {
            return ModelListResult.NotConfigured("Enter an API key to load models.");
        }

        var catalog = _registry.GetCatalog(settings.ServiceType);
        if (catalog is null)
        {
            return ModelListResult.NotConfigured("No model catalog is registered for this provider.");
        }

        if (!forceRefresh)
        {
            var cached = await GetCachedModelsAsync(settings, cancellationToken);
            if (cached is not null)
            {
                return ModelListResult.Success(cached);
            }
        }

        var result = await catalog.ListModelsAsync(settings, cancellationToken);

        if (result.Status == ModelListStatus.Success)
        {
            await CacheModelsAsync(settings, result.Models, cancellationToken);
        }

        return result;
    }

    #region Azure Table Storage Cache

    private async Task<List<string>?> GetCachedModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync(cancellationToken);

            var response = await tableClient.GetEntityIfExistsAsync<AIModelCacheEntity>(
                AIServiceTypes.ToStorageKey(settings.ServiceType),
                GetCacheRowKey(settings.ApiKey),
                cancellationToken: cancellationToken);

            if (response.HasValue && response.Value is not null)
            {
                var entity = response.Value;

                if (entity.LastFetched.HasValue &&
                    DateTimeOffset.UtcNow - entity.LastFetched.Value < CacheTtl)
                {
                    var models = JsonSerializer.Deserialize<List<string>>(entity.ModelsJson ?? "[]");
                    if (models is { Count: > 0 })
                        return models;
                }
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Failed to read model cache for {ServiceType}.", settings.ServiceType);
        }

        return null;
    }

    private async Task CacheModelsAsync(AIProviderSettings settings, IReadOnlyList<string> models, CancellationToken cancellationToken)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync(cancellationToken);

            var entity = new AIModelCacheEntity
            {
                PartitionKey = AIServiceTypes.ToStorageKey(settings.ServiceType),
                RowKey = GetCacheRowKey(settings.ApiKey),
                ModelsJson = JsonSerializer.Serialize(models),
                LastFetched = DateTimeOffset.UtcNow
            };

            await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache models for {ServiceType}.", settings.ServiceType);
        }
    }

    private static string GetCacheRowKey(string apiKey)
    {
        // Hash so the key itself is never stored; trimmed to avoid whitespace duplicates.
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey.Trim()));
        return Convert.ToHexString(hash)[..16];
    }

    #endregion
}

/// <summary>
/// Azure Table entity for caching AI model lists.
/// </summary>
public class AIModelCacheEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// JSON-serialized list of model IDs.
    /// </summary>
    public string? ModelsJson { get; set; }

    /// <summary>
    /// When the models were last fetched from the provider API.
    /// </summary>
    public DateTimeOffset? LastFetched { get; set; }
}
