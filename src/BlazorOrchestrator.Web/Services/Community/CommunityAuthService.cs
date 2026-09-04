using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorOrchestrator.Web.Services.Community;

public interface ICommunityAuthService
{
    Task<CommunityAuthState> GetStateAsync(string bdoUserId, CancellationToken ct = default);

    /// <summary>Builds the authorization URL and remembers the PKCE verifier for the pending request.</summary>
    Task<string> BuildAuthorizeUrlAsync(string bdoUserId, IEnumerable<string>? scopes = null, CancellationToken ct = default);

    Task<CommunityAuthState> CompleteCodeExchangeAsync(string bdoUserId, string code, string state, string? issuer, CancellationToken ct = default);

    Task<DeviceCodeChallenge> StartDeviceFlowAsync(string bdoUserId, IEnumerable<string>? scopes = null, CancellationToken ct = default);

    Task<DeviceCodePollResult> PollDeviceFlowAsync(string bdoUserId, string deviceCode, CancellationToken ct = default);

    /// <summary>Returns a valid access token, refreshing silently when it is close to expiry.</summary>
    Task<string?> GetValidAccessTokenAsync(string bdoUserId, CancellationToken ct = default);

    Task SignOutAsync(string bdoUserId, CancellationToken ct = default);
}

public sealed class CommunityAuthService(
    IHttpClientFactory httpClientFactory,
    CommunityTokenStore tokenStore,
    ICommunitySettingsService settingsService,
    ILogger<CommunityAuthService> logger) : ICommunityAuthService
{
    public const string HttpClientName = "community-auth";
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, PendingAuthorization> Pending = new();
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private sealed record PendingAuthorization(string BdoUserId, string CodeVerifier, string Issuer, string RedirectUri, DateTimeOffset CreatedAt);

    public async Task<CommunityAuthState> GetStateAsync(string bdoUserId, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);
        var tokens = await tokenStore.GetAsync(bdoUserId, options.Issuer, ct);

        return tokens is null
            ? CommunityAuthState.SignedOut
            : new CommunityAuthState
            {
                IsSignedIn = true,
                CjlUserId = tokens.CjlUserId,
                DisplayName = tokens.DisplayName,
                Email = tokens.Email,
                AvatarUrl = tokens.AvatarUrl,
                Scopes = tokens.Scopes,
                ExpiresAt = tokens.ExpiresAtUtc
            };
    }

    public async Task<string> BuildAuthorizeUrlAsync(string bdoUserId, IEnumerable<string>? scopes = null, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);

        var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));

        PurgeStalePendingRequests();
        Pending[state] = new PendingAuthorization(bdoUserId, codeVerifier, options.Issuer, options.RedirectUri, DateTimeOffset.UtcNow);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = options.RedirectUri,
            ["scope"] = string.Join(' ', scopes ?? CommunityOptions.Scopes.Default),
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["nonce"] = Base64Url(RandomNumberGenerator.GetBytes(16))
        };

        return QueryHelpers(options.AuthorizeEndpoint, query);
    }

    public async Task<CommunityAuthState> CompleteCodeExchangeAsync(
        string bdoUserId, string code, string state, string? issuer, CancellationToken ct = default)
    {
        if (!Pending.TryRemove(state, out var pending))
        {
            throw new InvalidOperationException("The authorization state is unknown or has expired.");
        }

        if (!string.Equals(pending.BdoUserId, bdoUserId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The authorization state belongs to a different user.");
        }

        // RFC 9207: when the authorization server returns iss, it must match the recorded issuer.
        if (!string.IsNullOrEmpty(issuer) && !string.Equals(issuer.TrimEnd('/'), pending.Issuer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The authorization response issuer does not match the expected issuer.");
        }

        var options = await settingsService.GetOptionsAsync(ct);

        var token = await PostTokenAsync(options.TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = options.ClientId,
            ["code"] = code,
            ["redirect_uri"] = pending.RedirectUri,
            ["code_verifier"] = pending.CodeVerifier
        }, ct);

        if (token.AccessToken is null)
        {
            throw new InvalidOperationException(token.ErrorDescription ?? token.Error ?? "The token exchange failed.");
        }

        await PersistAsync(bdoUserId, pending.Issuer, token, ct);
        return await GetStateAsync(bdoUserId, ct);
    }

    public async Task<DeviceCodeChallenge> StartDeviceFlowAsync(
        string bdoUserId, IEnumerable<string>? scopes = null, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);
        using var client = httpClientFactory.CreateClient(HttpClientName);

        using var response = await client.PostAsync(options.DeviceAuthorizationEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["scope"] = string.Join(' ', scopes ?? CommunityOptions.Scopes.Default)
            }), ct);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DeviceAuthorizationResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("The device authorization response could not be parsed.");

        return new DeviceCodeChallenge
        {
            DeviceCode = payload.DeviceCode ?? string.Empty,
            UserCode = payload.UserCode ?? string.Empty,
            VerificationUri = payload.VerificationUri ?? $"{options.BaseUrl.TrimEnd('/')}/device",
            VerificationUriComplete = payload.VerificationUriComplete,
            Interval = Math.Max(payload.Interval, 1),
            ExpiresIn = payload.ExpiresIn
        };
    }

    public async Task<DeviceCodePollResult> PollDeviceFlowAsync(string bdoUserId, string deviceCode, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);

        var token = await PostTokenAsync(options.TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = options.ClientId,
            ["device_code"] = deviceCode
        }, ct);

        if (token.AccessToken is not null)
        {
            await PersistAsync(bdoUserId, options.Issuer, token, ct);
            return new DeviceCodePollResult(true, false, await GetStateAsync(bdoUserId, ct), null);
        }

        return token.Error switch
        {
            "authorization_pending" or "slow_down" => new DeviceCodePollResult(false, true, null, null),
            _ => new DeviceCodePollResult(false, false, null, token.ErrorDescription ?? token.Error ?? "The device flow failed.")
        };
    }

    public async Task<string?> GetValidAccessTokenAsync(string bdoUserId, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);
        var tokens = await tokenStore.GetAsync(bdoUserId, options.Issuer, ct);

        if (tokens is null)
        {
            return null;
        }

        if (tokens.ExpiresAtUtc - DateTimeOffset.UtcNow > RefreshWindow)
        {
            return tokens.AccessToken;
        }

        if (tokens.RefreshToken is null)
        {
            await tokenStore.DeleteAsync(bdoUserId, options.Issuer, ct);
            return null;
        }

        await RefreshLock.WaitAsync(ct);
        try
        {
            // Another request may have refreshed while this one waited.
            var latest = await tokenStore.GetAsync(bdoUserId, options.Issuer, ct);
            if (latest is not null && latest.ExpiresAtUtc - DateTimeOffset.UtcNow > RefreshWindow)
            {
                return latest.AccessToken;
            }

            var token = await PostTokenAsync(options.TokenEndpoint, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = options.ClientId,
                ["refresh_token"] = latest?.RefreshToken ?? tokens.RefreshToken
            }, ct);

            if (token.AccessToken is null)
            {
                logger.LogWarning("Refreshing the community token failed: {Error}", token.Error);
                await tokenStore.DeleteAsync(bdoUserId, options.Issuer, ct);
                return null;
            }

            await PersistAsync(bdoUserId, options.Issuer, token, ct);
            return token.AccessToken;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public async Task SignOutAsync(string bdoUserId, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);
        var tokens = await tokenStore.GetAsync(bdoUserId, options.Issuer, ct);

        if (tokens?.RefreshToken is not null)
        {
            try
            {
                using var client = httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.PostAsync(options.RevocationEndpoint, new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = options.ClientId,
                        ["token"] = tokens.RefreshToken,
                        ["token_type_hint"] = "refresh_token"
                    }), ct);

                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Revoking the community refresh token failed; deleting it locally anyway");
            }
        }

        await tokenStore.DeleteAsync(bdoUserId, options.Issuer, ct);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<TokenResponse> PostTokenAsync(string endpoint, Dictionary<string, string> form, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(form), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new TokenResponse { Error = "empty_response" };
        }

        try
        {
            return JsonSerializer.Deserialize<TokenResponse>(body) ?? new TokenResponse { Error = "invalid_response" };
        }
        catch (JsonException)
        {
            return new TokenResponse { Error = "invalid_response" };
        }
    }

    private async Task PersistAsync(string bdoUserId, string issuer, TokenResponse token, CancellationToken ct)
    {
        var claims = ReadIdTokenClaims(token.IdToken);

        await tokenStore.SaveAsync(bdoUserId, issuer, new StoredCommunityTokens(
            token.AccessToken!,
            token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 3600 : token.ExpiresIn),
            token.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
            claims.GetValueOrDefault("sub"),
            claims.GetValueOrDefault("name"),
            claims.GetValueOrDefault("email"),
            claims.GetValueOrDefault("picture")), ct);
    }

    /// <summary>
    /// Reads display claims from the id token. The token was received directly from the authorization
    /// server over TLS in exchange for a PKCE-bound code, so it is used for display only.
    /// </summary>
    private static Dictionary<string, string> ReadIdTokenClaims(string? idToken)
    {
        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(idToken))
        {
            return claims;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return claims;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    claims[property.Name] = property.Value.GetString()!;
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // Display claims are optional; fall back to the profile endpoint.
        }

        return claims;
    }

    private static void PurgeStalePendingRequests()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        foreach (var entry in Pending.Where(p => p.Value.CreatedAt < cutoff).ToList())
        {
            Pending.TryRemove(entry.Key, out _);
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string QueryHelpers(string uri, Dictionary<string, string?> parameters)
    {
        var query = string.Join('&', parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

        return $"{uri}?{query}";
    }
}
