using Azure;
using Azure.Data.Tables;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for managing AI settings stored in Azure Table Storage.
/// </summary>
public class AISettingsService
{
    private readonly TableServiceClient _tableServiceClient;
    private const string TableName = "AISettings";
    private const string PartitionKey = "Settings";
    private const string RowKey = "Default";

    public AISettingsService(TableServiceClient tableServiceClient)
    {
        _tableServiceClient = tableServiceClient;
    }

    /// <summary>
    /// Retrieves the current AI settings from Azure Table Storage.
    /// </summary>
    public async Task<AISettings> GetSettingsAsync()
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync();

            var response = await tableClient.GetEntityIfExistsAsync<AISettingsEntity>(PartitionKey, RowKey);
            
            if (response.HasValue && response.Value != null)
            {
                return new AISettings
                {
                    AIServiceType = response.Value.AIServiceType ?? "OpenAI",
                    ApiKey = response.Value.ApiKey ?? "",
                    AIModel = response.Value.AIModel ?? "gpt-4-turbo-preview",
                    Endpoint = response.Value.Endpoint ?? "",
                    ApiVersion = response.Value.ApiVersion ?? "",
                    EmbeddingModel = response.Value.EmbeddingModel ?? ""
                };
            }
        }
        catch (RequestFailedException)
        {
            // Table or entity doesn't exist yet
        }

        return new AISettings();
    }

    /// <summary>
    /// Saves AI settings to Azure Table Storage.
    /// </summary>
    public async Task SaveSettingsAsync(AISettings settings)
    {
        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();

        var entity = new AISettingsEntity
        {
            PartitionKey = PartitionKey,
            RowKey = RowKey,
            AIServiceType = settings.AIServiceType,
            ApiKey = settings.ApiKey,
            AIModel = settings.AIModel,
            Endpoint = settings.Endpoint,
            ApiVersion = settings.ApiVersion,
            EmbeddingModel = settings.EmbeddingModel,
            Timestamp = DateTimeOffset.UtcNow
        };

        await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    /// <summary>
    /// Validates the API key format based on the service type.
    /// </summary>
    public static bool ValidateApiKey(string serviceType, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        if (serviceType == "OpenAI" && !apiKey.StartsWith("sk-"))
            return false;

        // Note: Anthropic and Google AI keys don't have a specific prefix requirement
        // Anthropic keys typically start with "sk-ant-" but this is not always the case
        // Google AI keys are alphanumeric and don't have a specific format

        return true;
    }
}

/// <summary>
/// Azure Table entity for AI settings.
/// </summary>
public class AISettingsEntity : ITableEntity
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
}
