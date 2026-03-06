using BlazorOrchestrator.Web.Data.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for validating user credentials against the AspNetUsers table.
/// Uses ASP.NET Core Identity's PasswordHasher for secure password verification.
/// </summary>
public class AuthService
{
    private readonly ApplicationDbContext _context;

    public AuthService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Validates a username and password against the database.
    /// Returns the user entity if credentials are valid, null otherwise.
    /// </summary>
    public async Task<AspNetUser?> ValidateCredentialsAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var user = await _context.AspNetUsers
            .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpperInvariant());

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
            return null;

        var passwordHasher = new PasswordHasher<AspNetUser>();
        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (result == PasswordVerificationResult.Failed)
            return null;

        // If the hash needs upgrade (e.g., algorithm change), rehash transparently
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await _context.SaveChangesAsync();
        }

        return user;
    }
}
