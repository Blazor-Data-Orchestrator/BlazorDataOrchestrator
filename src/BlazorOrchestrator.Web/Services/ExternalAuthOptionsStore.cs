using BlazorOrchestrator.Web.Models;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.Extensions.Options;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Stores runtime-editable external auth config and applies it to OAuth options.
/// Returns defensive copies so callers cannot mutate internal state without the lock.
/// </summary>
public sealed class ExternalAuthOptionsStore :
    IPostConfigureOptions<MicrosoftAccountOptions>,
    IPostConfigureOptions<GoogleOptions>
{
    private const string MicrosoftScheme = "Microsoft";
    private const string GoogleScheme = "Google";
    private const string NotConfigured = "not-configured";

    private readonly object _lock = new();
    private readonly IOptionsMonitorCache<MicrosoftAccountOptions> _microsoftCache;
    private readonly IOptionsMonitorCache<GoogleOptions> _googleCache;

    private AuthProviderConfig _microsoftConfig = new();
    private AuthProviderConfig _googleConfig = new();

    public ExternalAuthOptionsStore(
        IOptionsMonitorCache<MicrosoftAccountOptions> microsoftCache,
        IOptionsMonitorCache<GoogleOptions> googleCache)
    {
        _microsoftCache = microsoftCache;
        _googleCache = googleCache;
    }

    public AuthProviderConfig MicrosoftConfig
    {
        get
        {
            lock (_lock)
            {
                return Clone(_microsoftConfig);
            }
        }
    }

    public AuthProviderConfig GoogleConfig
    {
        get
        {
            lock (_lock)
            {
                return Clone(_googleConfig);
            }
        }
    }

    public void UpdateMicrosoft(AuthProviderConfig config)
    {
        lock (_lock)
        {
            _microsoftConfig = Clone(config);
        }

        _microsoftCache.TryRemove(MicrosoftScheme);
    }

    public void UpdateGoogle(AuthProviderConfig config)
    {
        lock (_lock)
        {
            _googleConfig = Clone(config);
        }

        _googleCache.TryRemove(GoogleScheme);
    }

    void IPostConfigureOptions<MicrosoftAccountOptions>.PostConfigure(string? name, MicrosoftAccountOptions options)
    {
        if (!string.Equals(name, MicrosoftScheme, StringComparison.Ordinal))
        {
            return;
        }

        var config = MicrosoftConfig;
        options.ClientId = NonEmpty(config.ClientId);
        options.ClientSecret = NonEmpty(config.ClientSecret);
        options.AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
        options.TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
        if (!options.Scope.Contains("openid")) options.Scope.Add("openid");
        if (!options.Scope.Contains("profile")) options.Scope.Add("profile");
        if (!options.Scope.Contains("email")) options.Scope.Add("email");
        options.SaveTokens = true;
        options.CallbackPath = "/signin-microsoft";
        options.AdditionalAuthorizationParameters["prompt"] = "login";
    }

    void IPostConfigureOptions<GoogleOptions>.PostConfigure(string? name, GoogleOptions options)
    {
        if (!string.Equals(name, GoogleScheme, StringComparison.Ordinal))
        {
            return;
        }

        var config = GoogleConfig;
        options.ClientId = NonEmpty(config.ClientId);
        options.ClientSecret = NonEmpty(config.ClientSecret);
        options.SaveTokens = true;
        options.AdditionalAuthorizationParameters["prompt"] = "login";
    }

    private static AuthProviderConfig Clone(AuthProviderConfig config)
    {
        return new AuthProviderConfig
        {
            Enabled = config.Enabled,
            ClientId = config.ClientId,
            ClientSecret = config.ClientSecret
        };
    }

    private static string NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? NotConfigured : value;
}
