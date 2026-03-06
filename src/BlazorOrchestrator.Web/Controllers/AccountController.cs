using BlazorOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public AccountController(AuthService authService)
    {
        _authService = authService;
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

        return LocalRedirect(returnUrl ?? "/");
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
}
