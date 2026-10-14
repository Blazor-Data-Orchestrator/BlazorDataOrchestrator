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
    /// <summary>
    /// The role that may run the Upgrade Wizard. Seeded by !SQL/01.00.00.sql.
    /// </summary>
    public const string AdministratorRole = "Administrator";

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

        var normalizedInput = username.ToUpperInvariant();

        // Try matching by username first, then fall back to email
        var user = await _context.AspNetUsers
            .FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedInput
                                   || u.NormalizedEmail == normalizedInput);

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

    /// <summary>
    /// Returns true when the user is a BlazorOrchestrator Administrator.
    /// Installations created before the Administrator role existed have no role members; until the
    /// Upgrade Wizard runs 01.00.00.sql (which makes those users Administrators), every existing
    /// user keeps the full access they had before.
    /// </summary>
    public async Task<bool> IsAdministratorAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var normalizedRole = AdministratorRole.ToUpperInvariant();

        var user = await _context.AspNetUsers
            .AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return false;

        if (user.Roles.Any(r => r.NormalizedName == normalizedRole))
            return true;

        var anyAdministrator = await _context.AspNetRoles
            .AsNoTracking()
            .Where(r => r.NormalizedName == normalizedRole)
            .AnyAsync(r => r.Users.Any());

        return !anyAdministrator;
    }

    /// <summary>
    /// Adds the user to the Administrator role, creating the role if it does not exist yet.
    /// </summary>
    public async Task AddToAdministratorRoleAsync(string userId)
    {
        var normalizedRole = AdministratorRole.ToUpperInvariant();

        var role = await _context.AspNetRoles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedRole);
        if (role == null)
        {
            role = new AspNetRole
            {
                Id = Guid.NewGuid().ToString(),
                Name = AdministratorRole,
                NormalizedName = normalizedRole,
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            _context.AspNetRoles.Add(role);
        }

        var user = await _context.AspNetUsers.Include(u => u.Roles).FirstAsync(u => u.Id == userId);
        if (!user.Roles.Any(r => r.Id == role.Id))
        {
            user.Roles.Add(role);
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Changes the password for a user after verifying the current password.
    /// Returns true if the password was changed successfully, false if the current password is incorrect.
    /// </summary>
    public async Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        var user = await ValidateCredentialsAsync(username, currentPassword);
        if (user == null)
            return false;

        var passwordHasher = new PasswordHasher<AspNetUser>();
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString();
        await _context.SaveChangesAsync();
        return true;
    }
}
