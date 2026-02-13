using Azure;
using Azure.Data.Tables;

namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Azure Table Storage entity for application settings.
/// PartitionKey = "AppSettings", RowKey = setting key (e.g. "TimezoneOffset").
/// </summary>
public class SettingsEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "AppSettings";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>The stored setting value, e.g. "-08:00".</summary>
    public string? Value { get; set; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; set; }
}
