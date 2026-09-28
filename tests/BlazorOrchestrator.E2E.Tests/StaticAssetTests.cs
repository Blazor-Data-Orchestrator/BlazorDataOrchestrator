using System.Text.RegularExpressions;
using Xunit;

namespace BlazorOrchestrator.E2E.Tests;

/// <summary>
/// Runs against a live Web app. Set BDO_WEB_URL (for example http://localhost:5000) to enable.
/// Point it at a non-Development host: in Development, MapStaticAssets serves every asset with no-cache.
/// </summary>
public class StaticAssetTests
{
    [Fact(DisplayName = "O9: site.js is fingerprinted and cached as immutable")]
    [Trait("Category", "Live")]
    public async Task SiteJs_IsFingerprinted_WithImmutableCaching()
    {
        var baseUrl = Environment.GetEnvironmentVariable("BDO_WEB_URL");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(baseUrl), "Set BDO_WEB_URL to run against a live Web app.");

        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl!) };

        var html = await client.GetStringAsync("/account/login");
        var match = Regex.Match(html, @"src=""(?<url>/?js/site\.(?<hash>[a-z0-9]+)\.js)""");
        Assert.True(match.Success, "site.js is not referenced through a fingerprinted URL.");

        using var response = await client.GetAsync(match.Groups["url"].Value.TrimStart('/'));
        response.EnsureSuccessStatusCode();
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }
}
