using System.Net;
using System.Net.Http.Headers;

namespace BlazorOrchestrator.Web.Services.Community;

/// <summary>
/// Attaches the current user's community access token and transparently retries once after a silent
/// refresh when the community site rejects the token.
/// </summary>
public sealed class CommunityAuthDelegatingHandler(
    ICommunityAuthService authService,
    ICommunityUserContext userContext,
    ILogger<CommunityAuthDelegatingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var userId = await userContext.GetUserIdAsync(cancellationToken);
        if (userId is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var token = await authService.GetValidAccessTokenAsync(userId, cancellationToken);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
        {
            return response;
        }

        logger.LogDebug("Community API returned 401; retrying once after a token refresh");
        response.Dispose();

        var refreshed = await authService.GetValidAccessTokenAsync(userId, cancellationToken);
        if (refreshed is null)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        var retry = await CloneAsync(request, cancellationToken);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var buffer = await request.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(buffer);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
