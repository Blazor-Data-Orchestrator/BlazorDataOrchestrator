using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Web.Constants;
using BlazorOrchestrator.Web.Models;

namespace BlazorOrchestrator.Web.Services;

public class AuthenticationSettingsService
{
    private readonly SettingsService _settingsService;

    public AuthenticationSettingsService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<AuthProviderConfig> GetMicrosoftConfigAsync()
    {
        var enabled = await _settingsService.GetOrDefaultAsync(AuthSettingKeys.MicrosoftEnabled, "false");
        var clientId = await _settingsService.GetAsync(AuthSettingKeys.MicrosoftClientId);
        var clientSecret = await _settingsService.GetAsync(AuthSettingKeys.MicrosoftClientSecret);

        return new AuthProviderConfig
        {
            Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase),
            ClientId = clientId ?? string.Empty,
            ClientSecret = clientSecret ?? string.Empty
        };
    }

    public async Task<AuthProviderConfig> GetGoogleConfigAsync()
    {
        var enabled = await _settingsService.GetOrDefaultAsync(AuthSettingKeys.GoogleEnabled, "false");
        var clientId = await _settingsService.GetAsync(AuthSettingKeys.GoogleClientId);
        var clientSecret = await _settingsService.GetAsync(AuthSettingKeys.GoogleClientSecret);

        return new AuthProviderConfig
        {
            Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase),
            ClientId = clientId ?? string.Empty,
            ClientSecret = clientSecret ?? string.Empty
        };
    }

    public async Task SaveMicrosoftConfigAsync(AuthProviderConfig config)
    {
        await _settingsService.SetAsync(AuthSettingKeys.MicrosoftEnabled, config.Enabled.ToString().ToLowerInvariant(), "Microsoft authentication enabled flag");
        await _settingsService.SetAsync(AuthSettingKeys.MicrosoftClientId, config.ClientId, "Microsoft OAuth Client ID");
        await _settingsService.SetAsync(AuthSettingKeys.MicrosoftClientSecret, config.ClientSecret, "Microsoft OAuth Client Secret");
    }

    public async Task SaveGoogleConfigAsync(AuthProviderConfig config)
    {
        await _settingsService.SetAsync(AuthSettingKeys.GoogleEnabled, config.Enabled.ToString().ToLowerInvariant(), "Google authentication enabled flag");
        await _settingsService.SetAsync(AuthSettingKeys.GoogleClientId, config.ClientId, "Google OAuth Client ID");
        await _settingsService.SetAsync(AuthSettingKeys.GoogleClientSecret, config.ClientSecret, "Google OAuth Client Secret");
    }
}
