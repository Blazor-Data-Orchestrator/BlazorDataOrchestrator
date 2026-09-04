using Microsoft.JSInterop;

namespace BlazorOrchestrator.Web.Services.Community;

public sealed record CommunitySignInOutcome(bool Succeeded, string? Message);

/// <summary>
/// Drives the interactive sign-in from Blazor components: opens the PKCE popup through JS interop and
/// waits for the callback page to post back.
/// </summary>
public sealed class CommunitySignInCoordinator(
    ICommunityAuthService authService,
    ICommunityUserContext userContext,
    ICommunitySettingsService settingsService,
    IJSRuntime jsRuntime,
    ILogger<CommunitySignInCoordinator> logger)
{
    public async Task<CommunityAuthState> GetStateAsync(CancellationToken ct = default)
    {
        var userId = await userContext.GetUserIdAsync(ct);
        return userId is null ? CommunityAuthState.SignedOut : await authService.GetStateAsync(userId, ct);
    }

    public async Task<bool> IsSignedInAsync(CancellationToken ct = default) =>
        (await GetStateAsync(ct)).IsSignedIn;

    /// <summary>Returns immediately when already signed in, otherwise opens the sign-in popup.</summary>
    public async Task<CommunitySignInOutcome> EnsureSignedInAsync(CancellationToken ct = default)
    {
        if (await IsSignedInAsync(ct))
        {
            return new CommunitySignInOutcome(true, null);
        }

        return await SignInAsync(ct);
    }

    public async Task<CommunitySignInOutcome> SignInAsync(CancellationToken ct = default)
    {
        var userId = await userContext.GetUserIdAsync(ct);
        if (userId is null)
        {
            return new CommunitySignInOutcome(false, "Sign in to Blazor Data Orchestrator first.");
        }

        var options = await settingsService.GetOptionsAsync(ct);
        if (options.PreferDeviceCodeFlow)
        {
            return new CommunitySignInOutcome(false, "device-code");
        }

        try
        {
            var url = await authService.BuildAuthorizeUrlAsync(userId, ct: ct);
            var result = await jsRuntime.InvokeAsync<PopupResult>("communityAuth.signIn", ct, url);

            return result.Status switch
            {
                "success" => new CommunitySignInOutcome(true, result.Message),
                "blocked" => new CommunitySignInOutcome(false, "The sign-in window was blocked. Allow popups or switch to the device code flow."),
                "cancelled" => new CommunitySignInOutcome(false, "Sign-in was cancelled."),
                _ => new CommunitySignInOutcome(false, result.Message ?? "Sign-in failed.")
            };
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "The community sign-in popup failed");
            return new CommunitySignInOutcome(false, "The sign-in window could not be opened.");
        }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var userId = await userContext.GetUserIdAsync(ct);
        if (userId is not null)
        {
            await authService.SignOutAsync(userId, ct);
        }
    }

    public async Task<DeviceCodeChallenge?> StartDeviceFlowAsync(CancellationToken ct = default)
    {
        var userId = await userContext.GetUserIdAsync(ct);
        return userId is null ? null : await authService.StartDeviceFlowAsync(userId, ct: ct);
    }

    public async Task<DeviceCodePollResult> PollDeviceFlowAsync(string deviceCode, CancellationToken ct = default)
    {
        var userId = await userContext.GetUserIdAsync(ct);
        return userId is null
            ? new DeviceCodePollResult(false, false, null, "No signed-in user.")
            : await authService.PollDeviceFlowAsync(userId, deviceCode, ct);
    }

    private sealed record PopupResult(string Status, string? Message);
}
