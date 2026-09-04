using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorOrchestrator.Web.Services.Community;

public interface ICommunityClient
{
    Task<CommunityPagedResult<CommunityPackageSummary>> SearchAsync(CommunitySearchQuery query, CancellationToken ct = default);
    Task<CommunityPackageDetail?> GetPackageAsync(string identifier, CancellationToken ct = default);
    Task<IReadOnlyList<CommunitySourceFile>> GetSourceAsync(string identifier, CancellationToken ct = default);
    Task<IReadOnlyList<CommunityCategory>> GetCategoriesAsync(CancellationToken ct = default);

    Task<CommunityRatingSummary?> GetRatingsAsync(string identifier, CancellationToken ct = default);
    Task<CommunityPagedResult<CommunityRating>> GetReviewsAsync(string identifier, int page, CancellationToken ct = default);
    Task<bool> UpsertRatingAsync(string identifier, int stars, string? review, CancellationToken ct = default);

    Task<CommunityPagedResult<CommunityDiscussion>> GetDiscussionsAsync(string identifier, int page, CancellationToken ct = default);
    Task<CommunityDiscussion?> GetThreadAsync(int discussionId, CancellationToken ct = default);
    Task<bool> PostDiscussionAsync(string identifier, string title, string body, CancellationToken ct = default);
    Task<bool> PostReplyAsync(int discussionId, string body, CancellationToken ct = default);

    Task<bool> SetFavoriteAsync(string identifier, bool isFavorite, CancellationToken ct = default);
    Task<bool> IsFavoriteAsync(string identifier, CancellationToken ct = default);
    Task<CommunityPagedResult<CommunityPackageSummary>> GetFavoritesAsync(int page, CancellationToken ct = default);

    Task<CommunityProfile?> GetProfileAsync(CancellationToken ct = default);
    Task<CommunityPagedResult<CommunitySubmission>> GetSubmissionsAsync(int page, CancellationToken ct = default);

    Task<CommunityPublishResult> PublishAsync(CommunityPublishRequest request, CancellationToken ct = default);
    Task<CommunityPublishResult> PublishVersionAsync(string identifier, CommunityPublishRequest request, CancellationToken ct = default);
    Task<bool> UpdatePackageAsync(string identifier, CommunityPackagePatch patch, CancellationToken ct = default);

    Task<CommunityDownloadTicket?> GetDownloadTicketAsync(string identifier, string? version, CancellationToken ct = default);
    Task RecordInstallAsync(string identifier, string version, bool succeeded, CancellationToken ct = default);
    Task<CommunityInstallation?> RegisterInstallationAsync(CancellationToken ct = default);

    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

public sealed class CommunityClient(
    HttpClient httpClient,
    ICommunitySettingsService settingsService,
    IMemoryCache cache,
    ILogger<CommunityClient> logger) : ICommunityClient
{
    public const string HttpClientName = "community-api";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SearchCacheLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DetailCacheLifetime = TimeSpan.FromMinutes(5);

    // ---------------------------------------------------------------- read

    public async Task<CommunityPagedResult<CommunityPackageSummary>> SearchAsync(CommunitySearchQuery query, CancellationToken ct = default)
    {
        var url = BuildUrl("api/v1/packages", new Dictionary<string, string?>
        {
            ["q"] = query.Query,
            ["category"] = query.Category,
            ["language"] = query.Language,
            ["tag"] = query.Tag,
            ["sort"] = query.Sort.ToString(),
            ["page"] = query.Page.ToString(),
            ["pageSize"] = query.PageSize.ToString()
        });

        return await GetCachedAsync<CommunityPagedResult<CommunityPackageSummary>>(url, SearchCacheLifetime, ct)
            ?? new CommunityPagedResult<CommunityPackageSummary>();
    }

    public Task<CommunityPackageDetail?> GetPackageAsync(string identifier, CancellationToken ct = default) =>
        GetCachedAsync<CommunityPackageDetail>($"api/v1/packages/{Uri.EscapeDataString(identifier)}", DetailCacheLifetime, ct);

    public async Task<IReadOnlyList<CommunitySourceFile>> GetSourceAsync(string identifier, CancellationToken ct = default) =>
        await GetCachedAsync<List<CommunitySourceFile>>(
            $"api/v1/packages/{Uri.EscapeDataString(identifier)}/source", DetailCacheLifetime, ct) ?? [];

    public async Task<IReadOnlyList<CommunityCategory>> GetCategoriesAsync(CancellationToken ct = default) =>
        await GetCachedAsync<List<CommunityCategory>>("api/v1/categories", DetailCacheLifetime, ct) ?? [];

    public Task<CommunityRatingSummary?> GetRatingsAsync(string identifier, CancellationToken ct = default) =>
        GetAsync<CommunityRatingSummary>($"api/v1/packages/{Uri.EscapeDataString(identifier)}/ratings", ct);

    public async Task<CommunityPagedResult<CommunityRating>> GetReviewsAsync(string identifier, int page, CancellationToken ct = default) =>
        await GetAsync<CommunityPagedResult<CommunityRating>>(
            $"api/v1/packages/{Uri.EscapeDataString(identifier)}/ratings/reviews?page={page}", ct)
        ?? new CommunityPagedResult<CommunityRating>();

    public async Task<CommunityPagedResult<CommunityDiscussion>> GetDiscussionsAsync(string identifier, int page, CancellationToken ct = default) =>
        await GetAsync<CommunityPagedResult<CommunityDiscussion>>(
            $"api/v1/packages/{Uri.EscapeDataString(identifier)}/discussions?page={page}", ct)
        ?? new CommunityPagedResult<CommunityDiscussion>();

    public Task<CommunityDiscussion?> GetThreadAsync(int discussionId, CancellationToken ct = default) =>
        GetAsync<CommunityDiscussion>($"api/v1/discussions/{discussionId}", ct);

    public Task<CommunityProfile?> GetProfileAsync(CancellationToken ct = default) =>
        GetAsync<CommunityProfile>("api/v1/me", ct);

    public async Task<CommunityPagedResult<CommunitySubmission>> GetSubmissionsAsync(int page, CancellationToken ct = default) =>
        await GetAsync<CommunityPagedResult<CommunitySubmission>>($"api/v1/me/submissions?page={page}", ct)
        ?? new CommunityPagedResult<CommunitySubmission>();

    public async Task<CommunityPagedResult<CommunityPackageSummary>> GetFavoritesAsync(int page, CancellationToken ct = default) =>
        await GetAsync<CommunityPagedResult<CommunityPackageSummary>>($"api/v1/me/favorites?page={page}", ct)
        ?? new CommunityPagedResult<CommunityPackageSummary>();

    public async Task<bool> IsFavoriteAsync(string identifier, CancellationToken ct = default)
    {
        var payload = await GetAsync<FavoriteState>($"api/v1/packages/{Uri.EscapeDataString(identifier)}/favorite", ct);
        return payload?.IsFavorite ?? false;
    }

    // ---------------------------------------------------------------- write

    public async Task<bool> UpsertRatingAsync(string identifier, int stars, string? review, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"api/v1/packages/{Uri.EscapeDataString(identifier)}/ratings",
            JsonContent.Create(new { stars, review }, options: Json), ct);

        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> PostDiscussionAsync(string identifier, string title, string body, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"api/v1/packages/{Uri.EscapeDataString(identifier)}/discussions",
            JsonContent.Create(new { title, body }, options: Json), ct);

        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> PostReplyAsync(int discussionId, string body, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"api/v1/discussions/{discussionId}/replies",
            JsonContent.Create(new { body }, options: Json), ct);

        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> SetFavoriteAsync(string identifier, bool isFavorite, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            isFavorite ? HttpMethod.Put : HttpMethod.Delete,
            $"api/v1/me/favorites/{Uri.EscapeDataString(identifier)}", content: null, ct);

        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> UpdatePackageAsync(string identifier, CommunityPackagePatch patch, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Patch,
            $"api/v1/packages/{Uri.EscapeDataString(identifier)}",
            JsonContent.Create(patch, options: Json), ct);

        return response?.IsSuccessStatusCode ?? false;
    }

    public Task<CommunityPublishResult> PublishAsync(CommunityPublishRequest request, CancellationToken ct = default) =>
        PublishCoreAsync("api/v1/packages", request, includeMetadata: true, ct);

    public Task<CommunityPublishResult> PublishVersionAsync(string identifier, CommunityPublishRequest request, CancellationToken ct = default) =>
        PublishCoreAsync($"api/v1/packages/{Uri.EscapeDataString(identifier)}/versions", request, includeMetadata: false, ct);

    public Task<CommunityDownloadTicket?> GetDownloadTicketAsync(string identifier, string? version, CancellationToken ct = default)
    {
        var url = $"api/v1/packages/{Uri.EscapeDataString(identifier)}/download";
        if (!string.IsNullOrEmpty(version))
        {
            url += $"?version={Uri.EscapeDataString(version)}";
        }

        return GetAsync<CommunityDownloadTicket>(url, ct);
    }

    public async Task RecordInstallAsync(string identifier, string version, bool succeeded, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);

        using var response = await SendAsync(HttpMethod.Post,
            $"api/v1/packages/{Uri.EscapeDataString(identifier)}/installs",
            JsonContent.Create(new
            {
                installationId = options.InstallationId,
                version,
                outcome = succeeded ? 0 : 1
            }, options: Json), ct);

        if (response is { IsSuccessStatusCode: false })
        {
            logger.LogDebug("Recording the community install returned {Status}", response.StatusCode);
        }
    }

    public async Task<CommunityInstallation?> RegisterInstallationAsync(CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);

        using var response = await SendAsync(HttpMethod.Post, "api/v1/installations",
            JsonContent.Create(new
            {
                installationId = options.InstallationId,
                displayName = options.InstallationDisplayName,
                productVersion = typeof(CommunityClient).Assembly.GetName().Version?.ToString()
            }, options: Json), ct);

        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<CommunityInstallation>(Json, ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "api/v1/categories", content: null, ct);
            return response?.IsSuccessStatusCode ?? false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- plumbing

    private async Task<CommunityPublishResult> PublishCoreAsync(
        string path, CommunityPublishRequest request, bool includeMetadata, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(request.PackageBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", request.FileName);

        form.Add(new StringContent(request.Version), "version");

        if (!string.IsNullOrEmpty(request.ReleaseNotes))
        {
            form.Add(new StringContent(request.ReleaseNotes), "releaseNotes");
        }

        if (request.InstallationId is { } installationId)
        {
            form.Add(new StringContent(installationId.ToString()), "installationId");
        }

        if (includeMetadata)
        {
            form.Add(new StringContent(request.Title), "title");
            form.Add(new StringContent(request.Description), "description");
            form.Add(new StringContent(request.Language), "language");
            form.Add(new StringContent("1"), "source"); // PublishSource.BdoPublishButton

            foreach (var category in request.Categories)
            {
                form.Add(new StringContent(category), "categories");
            }

            foreach (var tag in request.Tags)
            {
                form.Add(new StringContent(tag), "tags");
            }

            foreach (var framework in request.TargetFrameworks)
            {
                form.Add(new StringContent(framework), "targetFrameworks");
            }

            AddIfPresent(form, "license", request.License);
            AddIfPresent(form, "projectUrl", request.ProjectUrl);
            AddIfPresent(form, "repositoryUrl", request.RepositoryUrl);
            AddIfPresent(form, "sourceJobName", request.SourceJobName);

            if (request.ThumbnailBytes is { Length: > 0 } thumbnail)
            {
                var thumbnailContent = new ByteArrayContent(thumbnail);
                form.Add(thumbnailContent, "thumbnail", request.ThumbnailFileName ?? "thumbnail.png");
            }
        }

        using var response = await SendAsync(HttpMethod.Post, path, form, ct);
        if (response is null)
        {
            return CommunityPublishResult.Failure("unreachable", "The Community Jobs Library is unreachable.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            var payload = JsonSerializer.Deserialize<PublishResponsePayload>(body, Json);
            return new CommunityPublishResult
            {
                Succeeded = true,
                PackageId = payload?.PackageId ?? 0,
                PackageIdentifier = payload?.PackageIdentifier ?? string.Empty,
                Status = payload?.Status ?? "PendingReview",
                PackageUrl = await AbsoluteUrlAsync(payload?.PackageUrl, ct),
                SourceFilesExposed = payload?.SourceFilesExposed ?? 0
            };
        }

        return TranslateFailure(response.StatusCode, body);
    }

    private static CommunityPublishResult TranslateFailure(HttpStatusCode status, string body)
    {
        string? code = null;
        string? description = null;

        try
        {
            var error = JsonSerializer.Deserialize<ErrorPayload>(body, Json);
            code = error?.Error;
            description = error?.Description;
        }
        catch (JsonException)
        {
            // Fall through to the status-based message.
        }

        var message = description ?? status switch
        {
            HttpStatusCode.Unauthorized => "Sign in to the Community Jobs Library and try again.",
            HttpStatusCode.Forbidden => "This account is not allowed to publish that package.",
            HttpStatusCode.Conflict => "A package with that identifier already exists.",
            HttpStatusCode.RequestEntityTooLarge => "The package exceeds the 15 MB limit.",
            HttpStatusCode.TooManyRequests => "You have reached the publishing limit. Try again later.",
            _ => "Publishing failed. Please try again."
        };

        return CommunityPublishResult.Failure(code ?? status.ToString(), message);
    }

    private async Task<T?> GetCachedAsync<T>(string path, TimeSpan lifetime, CancellationToken ct) where T : class
    {
        var options = await settingsService.GetOptionsAsync(ct);
        var key = $"cjl:{options.BaseUrl}:{path}";

        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await GetAsync<T>(path, ct);
        if (value is not null)
        {
            cache.Set(key, value, lifetime);
        }

        return value;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null, ct);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var options = await settingsService.GetOptionsAsync(ct);

        using var request = new HttpRequestMessage(method, new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), path))
        {
            Content = content
        };

        try
        {
            return await httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "The community request to {Path} failed", path);
            return null;
        }
    }

    private async Task<string> AbsoluteUrlAsync(string? relative, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(relative, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var options = await settingsService.GetOptionsAsync(ct);
        return $"{options.BaseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";
    }

    private static void AddIfPresent(MultipartFormDataContent form, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            form.Add(new StringContent(value, Encoding.UTF8), name);
        }
    }

    private static string BuildUrl(string path, Dictionary<string, string?> parameters)
    {
        var query = string.Join('&', parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

        return query.Length == 0 ? path : $"{path}?{query}";
    }

    private sealed record FavoriteState(bool IsFavorite);

    private sealed record PublishResponsePayload(
        int PackageId, string? PackageIdentifier, string? Version, string? Status, string? PackageUrl, int SourceFilesExposed);

    private sealed record ErrorPayload(string? Error, string? Description);
}
