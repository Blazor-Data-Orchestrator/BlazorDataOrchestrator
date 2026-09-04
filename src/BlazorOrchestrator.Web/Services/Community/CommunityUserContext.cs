using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlazorOrchestrator.Web.Services.Community;

public interface ICommunityUserContext
{
    /// <summary>The signed-in BDO user id, used to scope stored community credentials.</summary>
    Task<string?> GetUserIdAsync(CancellationToken ct = default);

    Task<string> RequireUserIdAsync(CancellationToken ct = default);
}

public sealed class CommunityUserContext(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider? authenticationStateProvider = null) : ICommunityUserContext
{
    public async Task<string?> GetUserIdAsync(CancellationToken ct = default)
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true && authenticationStateProvider is not null)
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            principal = state.User;
        }

        return principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public async Task<string> RequireUserIdAsync(CancellationToken ct = default) =>
        await GetUserIdAsync(ct) ?? throw new InvalidOperationException("No signed-in Blazor Data Orchestrator user.");
}
