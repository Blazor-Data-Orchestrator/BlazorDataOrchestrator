using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Services.Community;

public interface ICommunitySettingsService
{
    Task<CommunityOptions> GetOptionsAsync(CancellationToken ct = default);
    Task SaveOptionsAsync(CommunityOptions options, CancellationToken ct = default);
    void Invalidate();
}

/// <summary>
/// Reads and writes the Community integration settings through the existing table-backed settings
/// store, falling back to appsettings values. Results are cached briefly to keep page loads cheap.
/// </summary>
public sealed class CommunitySettingsService(
    SettingsService settingsService,
    IConfiguration configuration,
    ILogger<CommunitySettingsService> logger) : ICommunitySettingsService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static CommunityOptions? _cached;
    private static DateTimeOffset _cachedAt;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    // Temporary kill switch: keeps the community nav item and publish button hidden until the
    // Community Jobs Library site goes live, regardless of stored/admin settings.
    private const bool CommunityFeaturesReleased = false;

    public async Task<CommunityOptions> GetOptionsAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
        {
            return _cached;
        }

        await Gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
            {
                return _cached;
            }

            var fallback = new CommunityOptions();
            configuration.GetSection(CommunityOptions.SectionName).Bind(fallback);

            var options = new CommunityOptions
            {
                BaseUrl = await GetAsync(CommunityOptions.SettingKeys.BaseUrl, fallback.BaseUrl),
                ClientId = await GetAsync(CommunityOptions.SettingKeys.ClientId, fallback.ClientId),
                EnableCommunityPublish = CommunityFeaturesReleased && await GetBoolAsync(CommunityOptions.SettingKeys.EnablePublish, fallback.EnableCommunityPublish),
                EnableCommunityBrowse = CommunityFeaturesReleased && await GetBoolAsync(CommunityOptions.SettingKeys.EnableBrowse, fallback.EnableCommunityBrowse),
                AllowImport = await GetBoolAsync(CommunityOptions.SettingKeys.AllowImport, fallback.AllowImport),
                PreferDeviceCodeFlow = await GetBoolAsync(CommunityOptions.SettingKeys.PreferDeviceCodeFlow, fallback.PreferDeviceCodeFlow),
                InstallationDisplayName = await GetAsync(CommunityOptions.SettingKeys.InstallationDisplayName, fallback.InstallationDisplayName),
                RedirectUri = await GetAsync(CommunityOptions.SettingKeys.RedirectUri, fallback.RedirectUri),
                InstallationId = await GetOrCreateInstallationIdAsync()
            };

            _cached = options;
            _cachedAt = DateTimeOffset.UtcNow;
            return options;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SaveOptionsAsync(CommunityOptions options, CancellationToken ct = default)
    {
        await settingsService.SetAsync(CommunityOptions.SettingKeys.BaseUrl, options.BaseUrl, "Community Jobs Library base URL");
        await settingsService.SetAsync(CommunityOptions.SettingKeys.ClientId, options.ClientId, "OAuth client id");
        await settingsService.SetAsync(CommunityOptions.SettingKeys.EnablePublish, options.EnableCommunityPublish.ToString(), "Show the Publish To Community Site button");
        await settingsService.SetAsync(CommunityOptions.SettingKeys.EnableBrowse, options.EnableCommunityBrowse.ToString(), "Show the Community Jobs Library menu item");
        await settingsService.SetAsync(CommunityOptions.SettingKeys.AllowImport, options.AllowImport.ToString(), "Allow pulling community jobs into this installation");
        await settingsService.SetAsync(CommunityOptions.SettingKeys.PreferDeviceCodeFlow, options.PreferDeviceCodeFlow.ToString(), "Use the device code flow instead of a browser popup");
        await settingsService.SetAsync(CommunityOptions.SettingKeys.InstallationDisplayName, options.InstallationDisplayName, "Name shown on the community site");
        await settingsService.SetAsync(CommunityOptions.SettingKeys.RedirectUri, options.RedirectUri, "OAuth redirect URI registered with the community site");

        Invalidate();
    }

    public void Invalidate() => _cached = null;

    private async Task<Guid> GetOrCreateInstallationIdAsync()
    {
        var stored = await settingsService.GetAsync(CommunityOptions.SettingKeys.InstallationId);
        if (Guid.TryParse(stored, out var existing) && existing != Guid.Empty)
        {
            return existing;
        }

        var created = Guid.NewGuid();
        await settingsService.SetAsync(CommunityOptions.SettingKeys.InstallationId, created.ToString(), "Stable identity for this installation");
        logger.LogInformation("Generated community installation id {InstallationId}", created);
        return created;
    }

    private async Task<string> GetAsync(string key, string fallback)
    {
        var value = await settingsService.GetAsync(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private async Task<bool> GetBoolAsync(string key, bool fallback)
    {
        var value = await settingsService.GetAsync(key);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
