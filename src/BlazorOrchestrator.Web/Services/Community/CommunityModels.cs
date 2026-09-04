using System.Text.Json.Serialization;

namespace BlazorOrchestrator.Web.Services.Community;

// ---------------------------------------------------------------- auth

public record CommunityAuthState
{
    public bool IsSignedIn { get; init; }
    public string? CjlUserId { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public DateTimeOffset? ExpiresAt { get; init; }

    public static CommunityAuthState SignedOut { get; } = new();
}

public record DeviceCodeChallenge
{
    public string DeviceCode { get; init; } = string.Empty;
    public string UserCode { get; init; } = string.Empty;
    public string VerificationUri { get; init; } = string.Empty;
    public string? VerificationUriComplete { get; init; }
    public int Interval { get; init; } = 5;
    public int ExpiresIn { get; init; } = 600;
}

public record DeviceCodePollResult(bool Completed, bool Pending, CommunityAuthState? State, string? Error);

internal sealed record TokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; init; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
    [JsonPropertyName("id_token")] public string? IdToken { get; init; }
    [JsonPropertyName("token_type")] public string? TokenType { get; init; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("scope")] public string? Scope { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; init; }
}

internal sealed record DeviceAuthorizationResponse
{
    [JsonPropertyName("device_code")] public string? DeviceCode { get; init; }
    [JsonPropertyName("user_code")] public string? UserCode { get; init; }
    [JsonPropertyName("verification_uri")] public string? VerificationUri { get; init; }
    [JsonPropertyName("verification_uri_complete")] public string? VerificationUriComplete { get; init; }
    [JsonPropertyName("interval")] public int Interval { get; init; } = 5;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; } = 600;
}

// ---------------------------------------------------------------- gallery

public record CommunityPagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

public record CommunityPackageSummary
{
    public int PackageId { get; init; }
    public string PackageIdentifier { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string? Language { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int DownloadCount { get; init; }
    public string? AuthorDisplayName { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public bool IsDeprecated { get; init; }
    public decimal? AverageRating { get; init; }
    public int RatingCount { get; init; }
}

public record CommunityDependency
{
    public string Name { get; init; } = string.Empty;
    public string? VersionRange { get; init; }
}

public record CommunityVersion
{
    public string Version { get; init; } = string.Empty;
    public DateTime PublishedAt { get; init; }
    public string? ReleaseNotes { get; init; }
}

public record CommunitySourceFile
{
    public string FileName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public int LineCount { get; init; }
    public bool IsTruncated { get; init; }
}

public record CommunityPackageDetail : CommunityPackageSummary
{
    public DateTime UpdatedAt { get; init; }
    public string? License { get; init; }
    public string? ProjectUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<CommunityDependency> Dependencies { get; init; } = [];
    public IReadOnlyList<CommunityVersion> Versions { get; init; } = [];
    public string? UsageExample { get; init; }
    public string? DeprecationMessage { get; init; }
    public int DiscussionCount { get; init; }
    public string? AuthorAvatarUrl { get; init; }
}

public record CommunityCategory
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? IconName { get; init; }
    public int PackageCount { get; init; }
}

public record CommunityRating
{
    public int RatingId { get; init; }
    public int Stars { get; init; }
    public string? Review { get; init; }
    public string AuthorDisplayName { get; init; } = string.Empty;
    public string? AuthorAvatarUrl { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CommunityRatingSummary
{
    public decimal? AverageRating { get; init; }
    public int TotalRatings { get; init; }
    public int Stars5 { get; init; }
    public int Stars4 { get; init; }
    public int Stars3 { get; init; }
    public int Stars2 { get; init; }
    public int Stars1 { get; init; }
    public IReadOnlyList<CommunityRating> RecentReviews { get; init; } = [];
}

public record CommunityDiscussionReply
{
    public int ReplyId { get; init; }
    public string Body { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record CommunityDiscussion
{
    public int DiscussionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public bool IsPinned { get; init; }
    public DateTime CreatedAt { get; init; }
    public int ReplyCount { get; init; }
    public IReadOnlyList<CommunityDiscussionReply> Replies { get; init; } = [];
}

public record CommunitySubmission
{
    public int PackageId { get; init; }
    public string PackageIdentifier { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string? Language { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? AiFlagReason { get; init; }
    public int DownloadCount { get; init; }
    public int InstallCount { get; init; }
    public decimal? AverageRating { get; init; }
    public int RatingCount { get; init; }
    public bool IsListed { get; init; }
    public bool IsDeprecated { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CommunityProfile
{
    public string UserId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public int UnapprovedCount { get; init; }
    public int MaxUnapproved { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

public record CommunityDownloadTicket
{
    public string PackageIdentifier { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public long FileSizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string? License { get; init; }
}

public record CommunityInstallation
{
    public Guid InstallationId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? ProductVersion { get; init; }
}

// ---------------------------------------------------------------- publish

public record CommunityPublishRequest
{
    public required byte[] PackageBytes { get; init; }
    public required string FileName { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public string Language { get; init; } = "C#";
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? License { get; init; }
    public string? ProjectUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];
    public string? ReleaseNotes { get; init; }
    public string? SourceJobName { get; init; }
    public Guid? InstallationId { get; init; }
    public byte[]? ThumbnailBytes { get; init; }
    public string? ThumbnailFileName { get; init; }
}

public record CommunityPublishResult
{
    public bool Succeeded { get; init; }
    public int PackageId { get; init; }
    public string PackageIdentifier { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PackageUrl { get; init; } = string.Empty;
    public int SourceFilesExposed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static CommunityPublishResult Failure(string code, string message) =>
        new() { Succeeded = false, ErrorCode = code, ErrorMessage = message };
}

public record CommunityPackagePatch
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Categories { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? License { get; init; }
    public string? ProjectUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public bool? IsListed { get; init; }
    public bool? IsDeprecated { get; init; }
    public string? DeprecationMessage { get; init; }
}

public enum CommunitySortOrder
{
    Relevance = 0,
    Downloads = 1,
    Rating = 2,
    Newest = 3,
    Updated = 4
}

public record CommunitySearchQuery
{
    public string? Query { get; init; }
    public string? Category { get; init; }
    public string? Language { get; init; }
    public string? Tag { get; init; }
    public CommunitySortOrder Sort { get; init; } = CommunitySortOrder.Relevance;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
