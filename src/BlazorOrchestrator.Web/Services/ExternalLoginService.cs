using BlazorOrchestrator.Web.Data;
using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

public class ExternalLoginService
{
    private readonly ApplicationDbContext _dbContext;

    public ExternalLoginService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Given an external login provider name and the authenticated claims,
    /// find an existing local AspNetUser and link the external identity.
    /// Returns null if no local account exists — users are never auto-created.
    /// </summary>
    public async Task<AspNetUser?> FindAndLinkUserAsync(
        string provider,
        string providerKey,
        string email,
        string displayName)
    {
        // 1. Check AspNetUserLogins for existing link
        var existingLogin = await _dbContext.AspNetUserLogins
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.LoginProvider == provider && l.ProviderKey == providerKey);

        if (existingLogin != null)
        {
            return existingLogin.User;
        }

        // 2. Check AspNetUsers by NormalizedEmail
        var normalizedEmail = email.ToUpperInvariant();
        var user = await _dbContext.AspNetUsers
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user == null)
        {
            // No local account exists — admin must pre-create the account
            return null;
        }

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
}
