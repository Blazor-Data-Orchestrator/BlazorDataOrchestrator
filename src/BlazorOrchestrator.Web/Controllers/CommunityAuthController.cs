using System.Security.Claims;
using System.Text;
using System.Net;
using BlazorOrchestrator.Web.Services.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorOrchestrator.Web.Controllers;

/// <summary>
/// Redirect target for the Community Jobs Library authorization code flow. The popup posts a message
/// to its opener and closes; tokens never leave the server.
/// </summary>
[ApiController]
[Route("community")]
[Authorize]
public class CommunityAuthController(
    ICommunityAuthService authService,
    ILogger<CommunityAuthController> logger) : ControllerBase
{
    [HttpGet("signin")]
    public async Task<IActionResult> SignIn(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Challenge();
        }

        var url = await authService.BuildAuthorizeUrlAsync(userId, ct: ct);
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? iss,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error))
        {
            logger.LogWarning("Community authorization returned {Error}", error);
            return Html(ResultPage(false, errorDescription ?? error));
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Html(ResultPage(false, "The authorization response was incomplete."));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Challenge();
        }

        try
        {
            await authService.CompleteCodeExchangeAsync(userId, code, state, iss, ct);
            return Html(ResultPage(true, "You are now signed in to the Community Jobs Library."));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Completing the community authorization failed");
            return Html(ResultPage(false, ex.Message));
        }
    }

    [HttpPost("signout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutOfCommunity(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
        {
            await authService.SignOutAsync(userId, ct);
        }

        return NoContent();
    }

    private ContentResult Html(string html) => Content(html, "text/html");

    private static string ResultPage(bool succeeded, string message)
    {
        var status = succeeded ? "success" : "error";
        var encoded = WebUtility.HtmlEncode(message);
        var payload = System.Text.Json.JsonSerializer.Serialize(new { source = "cjl-auth", status, message });

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<title>Community Jobs Library</title>")
          .Append("<style>body{font-family:Inter,-apple-system,\"Segoe UI\",sans-serif;display:flex;align-items:center;")
          .Append("justify-content:center;min-height:100vh;margin:0;background:#f6f8fa;color:#1b1f23}")
          .Append(".card{background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:32px;max-width:420px;text-align:center}")
          .Append("h1{font-size:18px;margin:0 0 8px}p{font-size:14px;color:#4a5568;margin:0}</style></head><body><div class=\"card\">")
          .Append("<h1>").Append(succeeded ? "Signed in" : "Sign-in failed").Append("</h1>")
          .Append("<p>").Append(encoded).Append("</p></div>")
          .Append("<script>try{if(window.opener){window.opener.postMessage(")
          .Append(payload)
          .Append(",window.location.origin);}}catch(e){}setTimeout(function(){window.close();},1200);</script>")
          .Append("</body></html>");

        return sb.ToString();
    }
}
