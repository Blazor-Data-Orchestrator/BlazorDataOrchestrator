using System.Security.Claims;
using BlazorOrchestrator.Web.Data;
using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

public class ExternalLoginService
{
    /// <summary>Microsoft Graph <c>userPrincipalName</c>; Entra only allows verified tenant domains here.</summary>
    public const string MicrosoftUpnClaimType = "urn:blazororchestrator:microsoft:upn";

    /// <summary>Google's <c>verified_email</c> / <c>email_verified</c> flag, normalised to "true" or "false".</summary>
    public const string GoogleEmailVerifiedClaimType = "urn:blazororchestrator:google:email_verified";

    private readonly ApplicationDbContext _dbContext;

    public ExternalLoginService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Returns the email address the provider vouches for, or null when there is none.
    /// The Microsoft <c>mail</c> attribute is set by tenant admins and is not verified, so it is
    /// never used for linking (SEC-004).
    /// </summary>
    public static string? GetLinkableEmail(string provider, IEnumerable<Claim> claims)
    {
        var list = claims as IReadOnlyCollection<Claim> ?? claims.ToList();

        if (string.Equals(provider, "Microsoft", StringComparison.Ordinal))
        {
            var upn = list.FirstOrDefault(c => c.Type == MicrosoftUpnClaimType)?.Value;
            // Guest accounts have a synthetic UPN such as user_contoso.com#EXT#@tenant.onmicrosoft.com.
            if (string.IsNullOrWhiteSpace(upn) || !upn.Contains('@') || upn.Contains("#EXT#", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return upn;
        }

        if (string.Equals(provider, "Google", StringComparison.Ordinal))
        {
            var verified = list.FirstOrDefault(c => c.Type == GoogleEmailVerifiedClaimType)?.Value;
            if (!string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var email = list.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            return string.IsNullOrWhiteSpace(email) ? null : email;
        }

        return null;
    }

    /// <summary>
    /// Given an external login provider name and the authenticated claims,
    /// find an existing local AspNetUser and link the external identity.
    /// Returns null if no local account exists — users are never auto-created.
    /// </summary>
    /// <param name="linkableEmail">
    /// An email the provider has verified (see <see cref="GetLinkableEmail"/>). When null, only an
    /// already-linked login is accepted.
    /// </param>
    public async Task<AspNetUser?> FindAndLinkUserAsync(
        string provider,
        string providerKey,
        string? linkableEmail,
        string displayName)
    {
        // 1. Check AspNetUserLogins for existing link
        var existingLogin = await _dbContext.AspNetUserLogins
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.LoginProvider == provider && l.ProviderKey == providerKey);

        if (existingLogin != null)
        {
            return IsLoginAllowed(existingLogin.User) ? existingLogin.User : null;
        }

        if (string.IsNullOrWhiteSpace(linkableEmail))
        {
            return null;
        }

        // 2. Check AspNetUsers by NormalizedEmail
        var normalizedEmail = linkableEmail.ToUpperInvariant();
        var user = await _dbContext.AspNetUsers
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user == null)
        {
            // No local account exists — admin must pre-create the account
            return null;
        }

        // Refuse to link external identities to disabled or locked accounts.
        if (!IsLoginAllowed(user)) return null;

        // 3. Create AspNetUserLogins entry to link
        var login = new AspNetUserLogin
        {
            LoginProvider = provider,
            ProviderKey = providerKey,
            ProviderDisplayName = displayName,
            UserId = user.Id
        };

        _dbContext.AspNetUserLogins.Add(login);
        await _dbContext.SaveChangesAsync();

        return user;
    }

    private static bool IsLoginAllowed(AspNetUser user)
    {
        if (!user.EmailConfirmed) return false;
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow) return false;
        return true;
    }
}
