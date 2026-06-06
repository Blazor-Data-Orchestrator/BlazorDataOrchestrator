using BlazorOrchestrator.Web.Data.Data;
using BlazorOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlazorOrchestrator.Web.Controllers;

/// <summary>
/// Handles user login and logout via HTTP endpoints.
/// Blazor Server components cannot set cookies directly (they run over SignalR),
/// so authentication must go through traditional HTTP request/response endpoints.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private readonly AuthService _authService;
    private readonly ExternalLoginService _externalLoginService;
    private readonly ApplicationDbContext _dbContext;
    private readonly AuthenticationSettings _authSettings;

    public AccountController(
        AuthService authService,
        ExternalLoginService externalLoginService,
        ApplicationDbContext dbContext,
        AuthenticationSettings authSettings)
    {
        _authService = authService;
        _externalLoginService = externalLoginService;
        _dbContext = dbContext;
        _authSettings = authSettings;
    }

    /// <summary>
    /// Processes login form submission. Validates credentials and issues an auth cookie.
    /// Uses a distinct path to avoid ambiguity with the Login.razor Blazor page.
    /// </summary>
    [HttpPost("/account/do-login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password, [FromForm] string? returnUrl = "/")
    {
        var user = await _authService.ValidateCredentialsAsync(username, password);
        if (user == null)
        {
            var errorReturnUrl = Uri.EscapeDataString(returnUrl ?? "/");
            return Redirect($"/account/login?error=Invalid+username+or+password&returnUrl={errorReturnUrl}");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? username),
            new(ClaimTypes.Email, user.Email ?? "")
        };
        await AddRoleClaimsAsync(claims, user.Id);

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });

        // Ensure returnUrl is local to prevent open-redirect and LocalRedirect errors
        if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = "/";

        return LocalRedirect(returnUrl);
    }

    /// <summary>
    /// Signs the user out and redirects to the login page.
    /// Supports both GET (for link/redirect) and POST (for form submission).
    /// </summary>
    [HttpGet("/account/logout")]
    [HttpPost("/account/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/account/login");
    }

    /// <summary>
    /// Initiates an external login challenge (redirects to Microsoft or Google).
    /// </summary>
    [HttpGet("/account/external-login")]
    public IActionResult ExternalLogin(string provider, string? returnUrl = "/")
    {
        var isConfigured = provider switch
        {
            "Microsoft" => _authSettings.IsMicrosoftConfigured,
            "Google" => _authSettings.IsGoogleConfigured,
            _ => false
        };

        if (!isConfigured)
        {
            var error = Uri.EscapeDataString($"{provider} authentication is not configured.");
            return Redirect($"/account/login?error={error}");
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action("ExternalLoginCallback", new { returnUrl }),
            Items = { { "provider", provider } }
        };
        return Challenge(properties, provider);
    }

    /// <summary>
    /// Handles the OAuth callback after external provider authentication.
    /// Links the external identity to an existing local account, or rejects if no account found.
    /// </summary>
    [HttpGet("/account/external-login-callback")]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = "/")
    {
        // Authenticate the external cookie.
        // The OAuth handler signs into the default cookie scheme, so read from it explicitly.
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal == null)
        {
            return Redirect("/account/login?error=External+authentication+failed");
        }

        // Extract claims
        var externalClaims = result.Principal.Claims.ToList();
        var providerKey = externalClaims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        var email = externalClaims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
                    ?? externalClaims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
        var name = externalClaims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? email ?? "";
        var provider = result.Properties?.Items.TryGetValue("provider", out var p) == true ? p : "Unknown";

        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(email))
        {
            return Redirect("/account/login?error=Could+not+retrieve+email+from+external+provider");
        }

        // Find or link the user
        var user = await _externalLoginService.FindAndLinkUserAsync(provider, providerKey, email, name);

        if (user == null)
        {
            var encodedError = Uri.EscapeDataString("No local account found for this email. Please contact an administrator to create your account.");
            return Redirect($"/account/login?error={encodedError}");
        }

        // Build enriched ClaimsPrincipal for the local cookie
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? name),
            new(ClaimTypes.Email, user.Email ?? email)
        };
        await AddRoleClaimsAsync(claims, user.Id);

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });

        // Validate returnUrl is local to prevent open redirect
        if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = "/";

        return LocalRedirect(returnUrl);
    }

    /// <summary>
    /// Loads role names for the user from AspNetUserRoles and appends them as ClaimTypes.Role
    /// claims so authorization attributes such as [Authorize(Roles = "Admin")] work.
    /// </summary>
    private async Task AddRoleClaimsAsync(List<Claim> claims, string userId)
    {
        var roleNames = await _dbContext.AspNetUsers
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Roles.Select(r => r.Name))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToListAsync();

        foreach (var roleName in roleNames)
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName!));
        }
    }
}
