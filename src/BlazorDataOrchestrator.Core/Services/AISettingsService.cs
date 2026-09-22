using Azure;
using Azure.Data.Tables;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Service for managing AI settings stored in Azure Table Storage.
/// One row is stored per service type so each provider keeps its own API key.
/// </summary>
public class AISettingsService
{
    private readonly TableServiceClient _tableServiceClient;
    private const string TableName = "AISettings";
    private const string PartitionKey = "Settings";
    private const string ActiveRowKey = "Active";
    private const string LegacyRowKey = "Default";

    public AISettingsService(TableServiceClient tableServiceClient)
    {
        _tableServiceClient = tableServiceClient;
    }

    /// <summary>
    /// Retrieves settings for every provider plus the active selection.
    /// Migrates a pre-existing single-row configuration on first read.
    /// </summary>
    public async Task<AISettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = new AISettings();

        try
        {
            var tableClient = await GetTableClientAsync(cancellationToken);

            var active = await tableClient.GetEntityIfExistsAsync<AIActiveServiceEntity>(
                PartitionKey, ActiveRowKey, cancellationToken: cancellationToken);

            if (!active.HasValue || active.Value is null)
            {
                await MigrateLegacySettingsAsync(tableClient, cancellationToken);

                active = await tableClient.GetEntityIfExistsAsync<AIActiveServiceEntity>(
                    PartitionKey, ActiveRowKey, cancellationToken: cancellationToken);
            }

            if (active.HasValue && active.Value is not null)
            {
                settings.ActiveServiceType = AIServiceTypes.ParseOrDefault(active.Value.ActiveServiceType);
            }

            foreach (var type in AIServiceTypes.All)
            {
                var response = await tableClient.GetEntityIfExistsAsync<AIProviderSettingsEntity>(
                    PartitionKey, AIServiceTypes.ToStorageKey(type), cancellationToken: cancellationToken);

                if (response.HasValue && response.Value is not null)
                {
                    settings.Providers[type] = ToModel(type, response.Value);
                }
            }
        }
        catch (RequestFailedException)
        {
            // Table or entity doesn't exist yet.
        }

        return settings;
    }

    /// <summary>
    /// Retrieves the stored settings for a single provider, or an empty instance when never saved.
    /// </summary>
    public async Task<AIProviderSettings> GetProviderSettingsAsync(ServiceKind type, CancellationToken cancellationToken = default)
    {
        try
        {
            var tableClient = await GetTableClientAsync(cancellationToken);

            var response = await tableClient.GetEntityIfExistsAsync<AIProviderSettingsEntity>(
                PartitionKey, AIServiceTypes.ToStorageKey(type), cancellationToken: cancellationToken);

            if (response.HasValue && response.Value is not null)
            {
                return ToModel(type, response.Value);
            }
        }
        catch (RequestFailedException)
        {
            // Table or entity doesn't exist yet.
        }

        return new AIProviderSettings { ServiceType = type };
    }

    /// <summary>
    /// Saves the settings for a single provider.
    /// </summary>
    public async Task SaveProviderSettingsAsync(AIProviderSettings settings, CancellationToken cancellationToken = default)
    {
        var tableClient = await GetTableClientAsync(cancellationToken);
        await SaveProviderInternalAsync(tableClient, settings, cancellationToken);
    }

    /// <summary>
    /// Records which provider is currently active.
    /// </summary>
    public async Task SetActiveServiceTypeAsync(ServiceKind type, CancellationToken cancellationToken = default)
    {
        var tableClient = await GetTableClientAsync(cancellationToken);
        await SaveActiveInternalAsync(tableClient, type, cancellationToken);
    }

    /// <summary>
    /// Saves the active selection and every provider held in the container.
    /// </summary>
    public async Task SaveSettingsAsync(AISettings settings, CancellationToken cancellationToken = default)
    {
        var tableClient = await GetTableClientAsync(cancellationToken);

        foreach (var pair in settings.Providers)
        {
            var provider = pair.Value;
            provider.ServiceType = pair.Key;
            await SaveProviderInternalAsync(tableClient, provider, cancellationToken);
        }

        await SaveActiveInternalAsync(tableClient, settings.ActiveServiceType, cancellationToken);
    }

    /// <summary>
    /// Removes the stored settings for a provider.
    /// </summary>
    public async Task ClearProviderSettingsAsync(ServiceKind type, CancellationToken cancellationToken = default)
    {
        var tableClient = await GetTableClientAsync(cancellationToken);

        await tableClient.DeleteEntityAsync(
            PartitionKey, AIServiceTypes.ToStorageKey(type), ETag.All, cancellationToken);
    }

    /// <summary>
    /// Validates the API key format based on the service type.
    /// </summary>
    public static bool ValidateApiKey(ServiceKind serviceType, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        // Anthropic and Google AI keys have no reliable prefix to validate against.
        if (serviceType == ServiceKind.OpenAI && !apiKey.StartsWith("sk-"))
            return false;

        return true;
    }

    /// <summary>
    /// Validates the API key format based on the service type name.
    /// </summary>
    public static bool ValidateApiKey(string serviceType, string apiKey)
        => ValidateApiKey(AIServiceTypes.ParseOrDefault(serviceType), apiKey);

    private async Task<TableClient> GetTableClientAsync(CancellationToken cancellationToken)
    {
        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync(cancellationToken);
        return tableClient;
    }

    private static async Task SaveProviderInternalAsync(TableClient tableClient, AIProviderSettings settings, CancellationToken cancellationToken)
    {
        var entity = new AIProviderSettingsEntity
        {
            PartitionKey = PartitionKey,
            RowKey = AIServiceTypes.ToStorageKey(settings.ServiceType),
            ApiKey = settings.ApiKey,
            AIModel = settings.AIModel,
            Endpoint = settings.Endpoint,
            ApiVersion = settings.ApiVersion,
            DeploymentPath = settings.DeploymentPath,
            EmbeddingModel = settings.EmbeddingModel,
            ApiProtocol = settings.ApiProtocol,
            Timestamp = DateTimeOffset.UtcNow
        };

        await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    private static async Task SaveActiveInternalAsync(TableClient tableClient, ServiceKind type, CancellationToken cancellationToken)
    {
        var entity = new AIActiveServiceEntity
        {
            PartitionKey = PartitionKey,
            RowKey = ActiveRowKey,
            ActiveServiceType = AIServiceTypes.ToStorageKey(type),
            Timestamp = DateTimeOffset.UtcNow
        };

        await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    /// <summary>
    /// Copies a pre-upgrade single-row configuration into the per-provider layout.
    /// The legacy row is left in place so the migration is idempotent and reversible.
    /// </summary>
    private static async Task MigrateLegacySettingsAsync(TableClient tableClient, CancellationToken cancellationToken)
    {
        var legacy = await tableClient.GetEntityIfExistsAsync<AILegacySettingsEntity>(
            PartitionKey, LegacyRowKey, cancellationToken: cancellationToken);

        if (!legacy.HasValue || legacy.Value is null)
        {
            return;
        }

        var type = AIServiceTypes.ParseOrDefault(legacy.Value.AIServiceType);

        await SaveProviderInternalAsync(tableClient, new AIProviderSettings
        {
            ServiceType = type,
            ApiKey = legacy.Value.ApiKey ?? "",
            AIModel = legacy.Value.AIModel ?? "",
            Endpoint = legacy.Value.Endpoint ?? "",
            ApiVersion = legacy.Value.ApiVersion ?? "",
            DeploymentPath = legacy.Value.DeploymentPath ?? "",
            EmbeddingModel = legacy.Value.EmbeddingModel ?? ""
        }, cancellationToken);

        await SaveActiveInternalAsync(tableClient, type, cancellationToken);
    }

    private static AIProviderSettings ToModel(ServiceKind type, AIProviderSettingsEntity entity) => new()
    {
        ServiceType = type,
        ApiKey = entity.ApiKey ?? "",
        AIModel = entity.AIModel ?? "",
        Endpoint = entity.Endpoint ?? "",
        ApiVersion = entity.ApiVersion ?? "",
        DeploymentPath = entity.DeploymentPath ?? "",
        EmbeddingModel = entity.EmbeddingModel ?? "",
        ApiProtocol = entity.ApiProtocol ?? ""
    };
}

/// <summary>
/// Azure Table entity for a single provider's settings. RowKey is the service storage key.
/// </summary>
public class AIProviderSettingsEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "Settings";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? ApiKey { get; set; }
    public string? AIModel { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiVersion { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? DeploymentPath { get; set; }

    /// <summary>Azure AI Foundry wire protocol. Null on rows written before the Foundry support.</summary>
    public string? ApiProtocol { get; set; }
}

/// <summary>
/// Azure Table entity recording which provider is currently active.
/// </summary>
public class AIActiveServiceEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "Settings";
    public string RowKey { get; set; } = "Active";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? ActiveServiceType { get; set; }
}

/// <summary>
/// Pre-upgrade single-row entity. Read-only; retained so migration stays reversible.
/// </summary>
public class AILegacySettingsEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "Settings";
    public string RowKey { get; set; } = "Default";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? AIServiceType { get; set; }
    public string? ApiKey { get; set; }
    public string? AIModel { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiVersion { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? DeploymentPath { get; set; }
}
