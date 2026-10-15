using Azure;
using Azure.Data.Tables;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Generic key-value settings service backed by Azure Table Storage.
/// Follows the same pattern as <see cref="AISettingsService"/>.
/// </summary>
public class SettingsService
{
    private readonly TableServiceClient _tableServiceClient;
    private const string TableName = "Settings";
    private const string PartitionKey = "AppSettings";

    public SettingsService(TableServiceClient tableServiceClient)
    {
        _tableServiceClient = tableServiceClient;
    }

    /// <summary>
    /// Gets a setting value by key. Returns null if not found.
    /// </summary>
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync();

            var response = await tableClient.GetEntityIfExistsAsync<SettingsEntity>(
                PartitionKey, key);

            if (response.HasValue && response.Value != null)
            {
                return response.Value.Value;
            }
        }
        catch (RequestFailedException)
        {
            // Table or entity doesn't exist yet — return null
        }

        return null;
    }

    /// <summary>
    /// Gets a setting value by key for decisions that must fail closed. Returns null only when
    /// the table or the setting does not exist (HTTP 404); any other storage failure throws, so a
    /// transient outage is never mistaken for "not set".
    /// </summary>
    public async Task<string?> GetStrictAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableServiceClient.GetTableClient(TableName)
                .GetEntityIfExistsAsync<SettingsEntity>(PartitionKey, key, cancellationToken: cancellationToken);
            return response.HasValue ? response.Value?.Value : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets a setting value by key, returning defaultValue if not found.
    /// </summary>
    public async Task<string> GetOrDefaultAsync(string key, string defaultValue)
    {
        var value = await GetAsync(key);
        return value ?? defaultValue;
    }

    /// <summary>
    /// Upserts a setting into Azure Table Storage.
    /// </summary>
    public async Task SetAsync(string key, string value, string? description = null)
    {
        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();

        var entity = new SettingsEntity
        {
            PartitionKey = PartitionKey,
            RowKey = key,
            Value = value,
            Description = description,
            Timestamp = DateTimeOffset.UtcNow
        };

        await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }
}
