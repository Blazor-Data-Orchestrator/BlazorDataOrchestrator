using System.ComponentModel.DataAnnotations;

namespace BlazorOrchestrator.Web.Models;

/// <summary>
/// Thrown when an operation would remove the last enabled admin from the system.
/// Surfaces as a friendly toast in the UI.
/// </summary>
public class AdminLockoutException : InvalidOperationException
{
    public AdminLockoutException(string message) : base(message) { }
}

public class AllowedUserListItem
{
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public List<string> LinkedProviders { get; set; } = new();
    public bool HasLocalPassword { get; set; }
}

public class AllowedUserDetail : AllowedUserListItem
{
    public List<AllowedUserLogin> Logins { get; set; } = new();
}

public class AllowedUserLogin
{
    public string LoginProvider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string? ProviderDisplayName { get; set; }
}

public class CreateAllowedUserRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256)]
    public string UserName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? DisplayName { get; set; }

    public bool IsAdmin { get; set; }
    public bool IsEnabled { get; set; } = true;

    public string? InitialPassword { get; set; }
}

public class UpdateAllowedUserRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256)]
    public string UserName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? DisplayName { get; set; }

    public bool IsAdmin { get; set; }
    public bool IsEnabled { get; set; } = true;
}
