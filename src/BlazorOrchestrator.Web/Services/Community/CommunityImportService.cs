using System.IO.Compression;
using System.Security.Cryptography;
using BlazorDataOrchestrator.Core;
using BlazorOrchestrator.Web.Services;

namespace BlazorOrchestrator.Web.Services.Community;

public sealed record CommunityImportRequest
{
    public required string PackageIdentifier { get; init; }
    public required string Version { get; init; }
    public required string JobName { get; init; }
    public string? JobEnvironment { get; init; }
    public int? OrganizationId { get; init; }
}

public sealed record CommunityImportResult(bool Succeeded, int? JobId, string? BlobName, string? ErrorMessage)
{
    public static CommunityImportResult Failure(string message) => new(false, null, null, message);
}

public interface ICommunityImportService
{
    Task<CommunityImportResult> ImportAsync(CommunityImportRequest request, CancellationToken ct = default);
}

/// <summary>
/// Downloads a community package and creates a disabled, unscheduled job that references it. The job
/// is never enabled automatically: the user must review the source before running third-party code.
/// </summary>
public sealed class CommunityImportService(
    ICommunityClient communityClient,
    ICommunitySettingsService settingsService,
    JobService jobService,
    JobManager jobManager,
    IHttpClientFactory httpClientFactory,
    ILogger<CommunityImportService> logger) : ICommunityImportService
{
    public const string DownloadHttpClientName = "community-download";
    private const long MaxPackageBytes = 15_728_640;

    public async Task<CommunityImportResult> ImportAsync(CommunityImportRequest request, CancellationToken ct = default)
    {
        var options = await settingsService.GetOptionsAsync(ct);
        if (!options.AllowImport)
        {
            return CommunityImportResult.Failure("Importing community jobs is disabled by your administrator.");
        }

        var ticket = await communityClient.GetDownloadTicketAsync(request.PackageIdentifier, request.Version, ct);
        if (ticket is null)
        {
            return CommunityImportResult.Failure("The community package could not be requested. Check that you are signed in.");
        }

        if (ticket.FileSizeBytes > MaxPackageBytes)
        {
            return CommunityImportResult.Failure("The community package exceeds the 15 MB limit.");
        }

        byte[] bytes;
        try
        {
            using var client = httpClientFactory.CreateClient(DownloadHttpClientName);
            bytes = await client.GetByteArrayAsync(ticket.DownloadUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Downloading community package {Identifier} failed", request.PackageIdentifier);
            await SafeRecordInstallAsync(request, succeeded: false, ct);
            return CommunityImportResult.Failure("The package could not be downloaded from the community site.");
        }

        if (bytes.LongLength > MaxPackageBytes)
        {
            return CommunityImportResult.Failure("The community package exceeds the 15 MB limit.");
        }

        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrEmpty(ticket.Sha256) &&
            !string.Equals(checksum, ticket.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Checksum mismatch importing {Identifier}: expected {Expected} got {Actual}",
                request.PackageIdentifier, ticket.Sha256, checksum);
            await SafeRecordInstallAsync(request, succeeded: false, ct);
            return CommunityImportResult.Failure("The downloaded package failed its integrity check and was discarded.");
        }

        if (!IsSafeArchive(bytes, out var archiveError))
        {
            await SafeRecordInstallAsync(request, succeeded: false, ct);
            return CommunityImportResult.Failure(archiveError);
        }

        var organizationId = request.OrganizationId ?? (await jobService.GetDefaultOrganizationAsync())?.Id;
        if (organizationId is null)
        {
            return CommunityImportResult.Failure("No organization is configured to own the imported job.");
        }

        var jobName = await ResolveJobNameAsync(request.JobName);

        var job = await jobService.CreateJobAsync(jobName, organizationId.Value, request.JobEnvironment);

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var blobName = await jobManager.UploadJobPackageAsync(job.Id, stream, $"{request.PackageIdentifier}.{ticket.Version}.nupkg");

            // Third-party code is never scheduled or enabled on import.
            job.JobEnabled = false;
            job.JobQueued = false;
            job.JobCodeFile = blobName;
            await jobService.UpdateJobAsync(job);

            await SafeRecordInstallAsync(request, succeeded: true, ct);

            logger.LogInformation("Imported community package {Identifier} v{Version} as job {JobId}",
                request.PackageIdentifier, ticket.Version, job.Id);

            return new CommunityImportResult(true, job.Id, blobName, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Importing community package {Identifier} failed after the job was created", request.PackageIdentifier);

            try
            {
                await jobService.DeleteJobAsync(job.Id);
            }
            catch (Exception cleanupEx)
            {
                logger.LogWarning(cleanupEx, "Rolling back the partially imported job {JobId} failed", job.Id);
            }

            await SafeRecordInstallAsync(request, succeeded: false, ct);
            return CommunityImportResult.Failure("The package was downloaded but could not be attached to a new job.");
        }
    }

    /// <summary>Rejects archives that are not readable zips or that contain traversal entries (Zip Slip).</summary>
    private static bool IsSafeArchive(byte[] bytes, out string error)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var hasNuspec = false;

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');

                if (name.StartsWith('/') || name.Contains("../", StringComparison.Ordinal) || Path.IsPathRooted(name))
                {
                    error = "The package contains unsafe file paths and was rejected.";
                    return false;
                }

                if (name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                {
                    hasNuspec = true;
                }
            }

            if (!hasNuspec)
            {
                error = "The package does not contain a .nuspec manifest.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (InvalidDataException)
        {
            error = "The downloaded file is not a valid NuGet package.";
            return false;
        }
    }

    private async Task<string> ResolveJobNameAsync(string desiredName)
    {
        var existing = await jobService.GetJobsAsync();
        var taken = existing.Select(j => j.JobName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(desiredName))
        {
            return desiredName;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{desiredName} ({suffix})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{desiredName} ({Guid.NewGuid():N})"[..Math.Min(desiredName.Length + 10, 128)];
    }

    private async Task SafeRecordInstallAsync(CommunityImportRequest request, bool succeeded, CancellationToken ct)
    {
        try
        {
            await communityClient.RecordInstallAsync(request.PackageIdentifier, request.Version, succeeded, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Recording the community install failed");
        }
    }
}
