using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Data;

/// <summary>
/// Where the applied schema version is recorded. BlazorOrchestrator keeps it as the
/// <c>SchemaVersion</c> setting in Azure Table Storage (read by <c>SystemStatusService</c>, the
/// setup gate and the Upgrade Wizard).
/// </summary>
public interface ISchemaVersionStore
{
    /// <summary>The recorded version, or null when none is recorded.</summary>
    Task<string?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string version, CancellationToken cancellationToken = default);
}

public sealed class SettingsSchemaVersionStore(SettingsService settingsService) : ISchemaVersionStore
{
    public const string SettingKey = "SchemaVersion";

    /// <summary>
    /// Null only when no version is recorded; a storage failure throws, so a transient outage is
    /// never mistaken for "no version" (which would re-run scripts or record the baseline).
    /// </summary>
    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        var value = await settingsService.GetStrictAsync(SettingKey, cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public Task SetAsync(string version, CancellationToken cancellationToken = default) =>
        settingsService.SetAsync(SettingKey, version, "Database schema version tracking");
}
