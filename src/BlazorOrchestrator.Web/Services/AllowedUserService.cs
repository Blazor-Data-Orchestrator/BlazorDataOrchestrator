using BlazorOrchestrator.Web.Data.Data;
using BlazorOrchestrator.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Administration service for managing the Allowed Users list backed by the ASP.NET Core
/// Identity tables (AspNetUsers, AspNetUserLogins, AspNetUserClaims, AspNetUserRoles).
/// </summary>
public class AllowedUserService
{
    private const string AdminRoleName = "Admin";
    private const string AdminRoleNormalized = "ADMIN";
    private const string DisplayNameClaimType = "name";

    private readonly ApplicationDbContext _db;
    private readonly ILogger<AllowedUserService> _logger;
    private readonly PasswordHasher<AspNetUser> _passwordHasher = new();

    public AllowedUserService(ApplicationDbContext db, ILogger<AllowedUserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AllowedUserListItem>> ListAsync(string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = BuildUserQuery(search);
        var users = await query
            .OrderBy(u => u.Email)
            .Skip(skip)
            .Take(take)
            .Select(u => new
            {
                User = u,
                DisplayName = u.AspNetUserClaims
                    .Where(c => c.ClaimType == DisplayNameClaimType)
                    .Select(c => c.ClaimValue)
                    .FirstOrDefault(),
                LoginProviders = u.AspNetUserLogins.Select(l => l.LoginProvider).ToList(),
                IsAdmin = u.Roles.Any(r => r.NormalizedName == AdminRoleNormalized)
            })
            .ToListAsync(ct);

        return users.Select(x => MapListItem(x.User, x.DisplayName, x.LoginProviders, x.IsAdmin)).ToList();
    }

    public Task<int> CountAsync(string? search, CancellationToken ct = default)
        => BuildUserQuery(search).CountAsync(ct);

    public async Task<AllowedUserDetail?> GetAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.AspNetUsers
            .Include(u => u.AspNetUserClaims)
            .Include(u => u.AspNetUserLogins)
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null) return null;

        return MapDetail(user);
    }

    public async Task<AllowedUserDetail> CreateAsync(CreateAllowedUserRequest request, CancellationToken ct = default)
    {
        Validate(request.Email, request.UserName, request.InitialPassword);

        var normalizedEmail = request.Email.ToUpperInvariant();
        var normalizedUserName = request.UserName.ToUpperInvariant();

        if (await _db.AspNetUsers.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
            throw new InvalidOperationException($"A user with email '{request.Email}' already exists.");

        if (await _db.AspNetUsers.AnyAsync(u => u.NormalizedUserName == normalizedUserName, ct))
            throw new InvalidOperationException($"A user with username '{request.UserName}' already exists.");

        var user = new AspNetUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = request.Email,
            NormalizedEmail = normalizedEmail,
            UserName = request.UserName,
            NormalizedUserName = normalizedUserName,
            EmailConfirmed = request.IsEnabled,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            AccessFailedCount = 0,
            PhoneNumberConfirmed = false,
            TwoFactorEnabled = false
        };

        if (!string.IsNullOrEmpty(request.InitialPassword))
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.InitialPassword);
        }

        _db.AspNetUsers.Add(user);

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            _db.AspNetUserClaims.Add(new AspNetUserClaim
            {
                UserId = user.Id,
                ClaimType = DisplayNameClaimType,
                ClaimValue = request.DisplayName
            });
        }

        if (request.IsAdmin)
        {
            var role = await EnsureAdminRoleAsync(ct);
            user.Roles.Add(role);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created allowed user {UserId} ({Email})", user.Id, user.Email);

        return (await GetAsync(user.Id, ct))!;
    }

    public async Task<AllowedUserDetail> UpdateAsync(string userId, UpdateAllowedUserRequest request, CancellationToken ct = default)
    {
        Validate(request.Email, request.UserName, password: null);

        var user = await _db.AspNetUsers
            .Include(u => u.AspNetUserClaims)
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var normalizedEmail = request.Email.ToUpperInvariant();
        var normalizedUserName = request.UserName.ToUpperInvariant();

        if (await _db.AspNetUsers.AnyAsync(u => u.Id != userId && u.NormalizedEmail == normalizedEmail, ct))
            throw new InvalidOperationException($"Another user already uses email '{request.Email}'.");

        if (await _db.AspNetUsers.AnyAsync(u => u.Id != userId && u.NormalizedUserName == normalizedUserName, ct))
            throw new InvalidOperationException($"Another user already uses username '{request.UserName}'.");

        var emailChanged = !string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal);

        user.Email = request.Email;
        user.NormalizedEmail = normalizedEmail;
        user.UserName = request.UserName;
        user.NormalizedUserName = normalizedUserName;
        user.EmailConfirmed = request.IsEnabled;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (emailChanged)
            user.SecurityStamp = Guid.NewGuid().ToString();

        // Display name claim
        var displayClaim = user.AspNetUserClaims.FirstOrDefault(c => c.ClaimType == DisplayNameClaimType);
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            if (displayClaim != null) _db.AspNetUserClaims.Remove(displayClaim);
        }
        else if (displayClaim == null)
        {
            _db.AspNetUserClaims.Add(new AspNetUserClaim
            {
                UserId = user.Id,
                ClaimType = DisplayNameClaimType,
                ClaimValue = request.DisplayName
            });
        }
        else
        {
            displayClaim.ClaimValue = request.DisplayName;
        }

        // Admin role
        var hadAdmin = user.Roles.Any(r => r.NormalizedName == AdminRoleNormalized);
        if (hadAdmin && !request.IsAdmin)
        {
            await GuardLastAdminAsync(userId, ct);
            var existing = user.Roles.First(r => r.NormalizedName == AdminRoleNormalized);
            user.Roles.Remove(existing);
        }
        else if (!hadAdmin && request.IsAdmin)
        {
            var role = await EnsureAdminRoleAsync(ct);
            user.Roles.Add(role);
        }

        // Enabled toggle — if disabling the last admin, block.
        if (!request.IsEnabled)
        {
            await GuardLastAdminAsync(userId, ct, includeDisable: true);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated allowed user {UserId}", user.Id);

        return (await GetAsync(user.Id, ct))!;
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.AspNetUsers
            .Include(u => u.AspNetUserClaims)
            .Include(u => u.AspNetUserLogins)
            .Include(u => u.AspNetUserTokens)
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        if (user.Roles.Any(r => r.NormalizedName == AdminRoleNormalized))
            await GuardLastAdminAsync(userId, ct);

        _db.AspNetUserClaims.RemoveRange(user.AspNetUserClaims);
        _db.AspNetUserLogins.RemoveRange(user.AspNetUserLogins);
        _db.AspNetUserTokens.RemoveRange(user.AspNetUserTokens);
        user.Roles.Clear();
        _db.AspNetUsers.Remove(user);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted allowed user {UserId}", userId);
    }

    public async Task SetPasswordAsync(string userId, string? newPassword, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(newPassword))
            ValidatePassword(newPassword);

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        user.PasswordHash = string.IsNullOrEmpty(newPassword)
            ? null
            : _passwordHasher.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Password {Action} for user {UserId}",
            string.IsNullOrEmpty(newPassword) ? "cleared" : "reset", userId);
    }

    public async Task SetLockoutAsync(string userId, DateTimeOffset? lockoutEnd, CancellationToken ct = default)
    {
        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        user.LockoutEnd = lockoutEnd;
        user.LockoutEnabled = true;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Lockout {Action} for user {UserId}",
            lockoutEnd == null ? "cleared" : "set", userId);
    }

    public async Task SetEnabledAsync(string userId, bool enabled, CancellationToken ct = default)
    {
        if (!enabled)
            await GuardLastAdminAsync(userId, ct, includeDisable: true);

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        user.EmailConfirmed = enabled;
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        await _db.SaveChangesAsync(ct);
    }

    public async Task SetIsAdminAsync(string userId, bool isAdmin, CancellationToken ct = default)
    {
        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var hasAdmin = user.Roles.Any(r => r.NormalizedName == AdminRoleNormalized);
        if (hasAdmin == isAdmin) return;

        if (!isAdmin)
        {
            await GuardLastAdminAsync(userId, ct);
            var existing = user.Roles.First(r => r.NormalizedName == AdminRoleNormalized);
            user.Roles.Remove(existing);
        }
        else
        {
            var role = await EnsureAdminRoleAsync(ct);
            user.Roles.Add(role);
        }

        user.SecurityStamp = Guid.NewGuid().ToString();
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnlinkProviderAsync(string userId, string provider, string providerKey, CancellationToken ct = default)
    {
        var login = await _db.AspNetUserLogins.FirstOrDefaultAsync(
            l => l.UserId == userId && l.LoginProvider == provider && l.ProviderKey == providerKey, ct);

        if (login == null) return;

        _db.AspNetUserLogins.Remove(login);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Unlinked provider {Provider} from user {UserId}", provider, userId);
    }

    /// <summary>
    /// Ensures the Admin role exists. If no enabled user is assigned to it, promotes every
    /// existing enabled user so the system isn't locked out of the new admin UI.
    /// Intended to be called once at application startup.
    /// </summary>
    public async Task BootstrapAdminRoleAsync(CancellationToken ct = default)
    {
        var role = await EnsureAdminRoleAsync(ct);

        var hasAnyAdmin = await _db.AspNetUsers
            .AnyAsync(u => u.EmailConfirmed && u.Roles.Any(r => r.NormalizedName == AdminRoleNormalized), ct);

        if (hasAnyAdmin)
        {
            await _db.SaveChangesAsync(ct);
            return;
        }

        var candidates = await _db.AspNetUsers
            .Include(u => u.Roles)
            .Where(u => u.EmailConfirmed)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No enabled users exist; Admin role created but unassigned. The /admin/users page will be inaccessible until an admin row is added directly to the database.");
            await _db.SaveChangesAsync(ct);
            return;
        }

        foreach (var u in candidates)
        {
            if (!u.Roles.Any(r => r.NormalizedName == AdminRoleNormalized))
                u.Roles.Add(role);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Bootstrapped Admin role: promoted {Count} existing enabled user(s).", candidates.Count);
    }

    // ---------- helpers ----------

    private IQueryable<AspNetUser> BuildUserQuery(string? search)
    {
        var query = _db.AspNetUsers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();
            query = query.Where(u =>
                (u.NormalizedEmail != null && u.NormalizedEmail.Contains(s)) ||
                (u.NormalizedUserName != null && u.NormalizedUserName.Contains(s)));
        }
        return query;
    }

    private async Task<AspNetRole> EnsureAdminRoleAsync(CancellationToken ct)
    {
        var role = await _db.AspNetRoles
            .FirstOrDefaultAsync(r => r.NormalizedName == AdminRoleNormalized, ct);

        if (role != null) return role;

        role = new AspNetRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = AdminRoleName,
            NormalizedName = AdminRoleNormalized,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        _db.AspNetRoles.Add(role);
        return role;
    }

    private async Task GuardLastAdminAsync(string userId, CancellationToken ct, bool includeDisable = false)
    {
        var adminCount = await _db.AspNetUsers
            .Where(u => u.Id != userId)
            .Where(u => u.Roles.Any(r => r.NormalizedName == AdminRoleNormalized))
            .Where(u => u.EmailConfirmed)
            .CountAsync(ct);

        if (adminCount == 0)
        {
            var action = includeDisable ? "disable" : "remove";
            throw new AdminLockoutException(
                $"Cannot {action} the last enabled administrator. Promote another user first.");
        }
    }

    private static void Validate(string email, string userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");
        if (string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("Username is required.");
        if (userName.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Username cannot contain whitespace.");
        if (!string.IsNullOrEmpty(password))
            ValidatePassword(password);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12)
            throw new InvalidOperationException("Password must be at least 12 characters.");
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit))
            throw new InvalidOperationException(
                "Password must contain upper, lower, digit, and symbol characters.");
    }

    private static AllowedUserListItem MapListItem(AspNetUser u, string? displayName, List<string> providers, bool isAdmin)
        => new()
        {
            Id = u.Id,
            Email = u.Email,
            UserName = u.UserName,
            DisplayName = displayName,
            IsAdmin = isAdmin,
            IsEnabled = u.EmailConfirmed,
            IsLocked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow,
            LockoutEnd = u.LockoutEnd,
            LinkedProviders = providers.Distinct().OrderBy(p => p).ToList(),
            HasLocalPassword = !string.IsNullOrEmpty(u.PasswordHash)
        };

    private static AllowedUserDetail MapDetail(AspNetUser u)
    {
        var displayName = u.AspNetUserClaims
            .FirstOrDefault(c => c.ClaimType == DisplayNameClaimType)?.ClaimValue;
        var providers = u.AspNetUserLogins.Select(l => l.LoginProvider).ToList();
        var isAdmin = u.Roles.Any(r => r.NormalizedName == AdminRoleNormalized);

        var listItem = MapListItem(u, displayName, providers, isAdmin);
        return new AllowedUserDetail
        {
            Id = listItem.Id,
            Email = listItem.Email,
            UserName = listItem.UserName,
            DisplayName = listItem.DisplayName,
            IsAdmin = listItem.IsAdmin,
            IsEnabled = listItem.IsEnabled,
            IsLocked = listItem.IsLocked,
            LockoutEnd = listItem.LockoutEnd,
            LinkedProviders = listItem.LinkedProviders,
            HasLocalPassword = listItem.HasLocalPassword,
            Logins = u.AspNetUserLogins.Select(l => new AllowedUserLogin
            {
                LoginProvider = l.LoginProvider,
                ProviderKey = l.ProviderKey,
                ProviderDisplayName = l.ProviderDisplayName
            }).ToList()
        };
    }
}
