using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorOrchestrator.Web.Services.Community;

internal sealed class CommunityTokenEntity : ITableEntity
{
    /// <summary>The BDO user id.</summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>The issuer, so credentials are never reused across authorization servers.</summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? AccessTokenCipher { get; set; }
    public string? RefreshTokenCipher { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string? Scopes { get; set; }
    public string? CjlUserId { get; set; }
    public string? CjlDisplayName { get; set; }
    public string? CjlEmail { get; set; }
    public string? CjlAvatarUrl { get; set; }
}

public sealed record StoredCommunityTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string[] Scopes,
    string? CjlUserId,
    string? DisplayName,
    string? Email,
    string? AvatarUrl);

/// <summary>
/// Persists CJL tokens server-side, encrypted with the data protection stack. Tokens never reach the
/// browser and are keyed by issuer so a change of authorization server forces re-authentication.
/// </summary>
public sealed class CommunityTokenStore(
    TableServiceClient tableServiceClient,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<CommunityTokenStore> logger)
{
    private const string TableName = "CommunityTokens";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("CJL.Tokens.v1");

    public async Task<StoredCommunityTokens?> GetAsync(string bdoUserId, string issuer, CancellationToken ct = default)
    {
        var table = await GetTableAsync(ct);
        var response = await table.GetEntityIfExistsAsync<CommunityTokenEntity>(bdoUserId, Key(issuer), cancellationToken: ct);

        if (!response.HasValue || response.Value is null || response.Value.AccessTokenCipher is null)
        {
            return null;
        }

        var entity = response.Value;

        try
        {
            return new StoredCommunityTokens(
                _protector.Unprotect(entity.AccessTokenCipher),
                entity.RefreshTokenCipher is null ? null : _protector.Unprotect(entity.RefreshTokenCipher),
                entity.ExpiresAtUtc,
                entity.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
                entity.CjlUserId,
                entity.CjlDisplayName,
                entity.CjlEmail,
                entity.CjlAvatarUrl);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // Key ring rotated or payload tampered with; force a fresh sign-in.
            logger.LogWarning(ex, "Stored community tokens for {UserId} could not be decrypted and were discarded", bdoUserId);
            await DeleteAsync(bdoUserId, issuer, ct);
            return null;
        }
    }

    public async Task SaveAsync(string bdoUserId, string issuer, StoredCommunityTokens tokens, CancellationToken ct = default)
    {
        var table = await GetTableAsync(ct);

        await table.UpsertEntityAsync(new CommunityTokenEntity
        {
            PartitionKey = bdoUserId,
            RowKey = Key(issuer),
            AccessTokenCipher = _protector.Protect(tokens.AccessToken),
            RefreshTokenCipher = tokens.RefreshToken is null ? null : _protector.Protect(tokens.RefreshToken),
            ExpiresAtUtc = tokens.ExpiresAtUtc,
            Scopes = string.Join(' ', tokens.Scopes),
            CjlUserId = tokens.CjlUserId,
            CjlDisplayName = tokens.DisplayName,
            CjlEmail = tokens.Email,
            CjlAvatarUrl = tokens.AvatarUrl
        }, TableUpdateMode.Replace, ct);
    }

    public async Task DeleteAsync(string bdoUserId, string issuer, CancellationToken ct = default)
    {
        var table = await GetTableAsync(ct);
        await table.DeleteEntityAsync(bdoUserId, Key(issuer), ETag.All, ct);
    }

    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        var table = tableServiceClient.GetTableClient(TableName);
        await table.CreateIfNotExistsAsync(ct);
        return table;
    }

    /// <summary>Table row keys cannot contain '/', '\', '#' or '?'.</summary>
    private static string Key(string issuer) =>
        issuer.Replace('/', '_').Replace('\\', '_').Replace('#', '_').Replace('?', '_');
}
