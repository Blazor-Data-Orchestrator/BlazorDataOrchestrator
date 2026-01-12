using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for handling Azure Blob Storage operations for NuGet packages.
/// Shared by Web (upload) and Agent (download) projects.
/// </summary>
public class JobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "jobs";

    public JobStorageService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
        _containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    /// <summary>
    /// Ensures the container exists. Should be called at startup.
    /// </summary>
    public async Task EnsureContainerExistsAsync()
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
    }

    /// <summary>
    /// Uploads a NuGet package to Azure Blob Storage.
    /// Generates a unique filename: {JobId}_{Guid}_{timestamp}.nupkg
    /// </summary>
    /// <param name="jobId">The job ID for naming</param>
    /// <param name="fileStream">The file stream to upload</param>
    /// <param name="originalFileName">The original filename for extension extraction</param>
    /// <returns>The unique blob name stored in Job.JobCodeFile</returns>
    public async Task<string> UploadPackageAsync(int jobId, Stream fileStream, string originalFileName)
    {
        await EnsureContainerExistsAsync();

        // Generate unique filename: {JobId}_{Guid}_{timestamp}.nupkg
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".nupkg";
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var uniqueName = $"{jobId}_{Guid.NewGuid():N}_{timestamp}{extension}";

        var blobClient = _containerClient.GetBlobClient(uniqueName);

        // Reset stream position if possible
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        await blobClient.UploadAsync(fileStream, new BlobHttpHeaders
        {
            ContentType = "application/octet-stream"
        });

        return uniqueName;
    }

    /// <summary>
    /// Updates (replaces) an existing package. Deletes the old one and uploads the new one.
    /// </summary>
    /// <param name="jobId">The job ID for naming</param>
    /// <param name="existingBlobName">The existing blob name to delete (can be null)</param>
    /// <param name="fileStream">The new file stream to upload</param>
    /// <param name="originalFileName">The original filename for extension extraction</param>
    /// <returns>The new unique blob name</returns>
    public async Task<string> UpdatePackageAsync(int jobId, string? existingBlobName, Stream fileStream, string originalFileName)
    {
        // Delete existing package if it exists
        if (!string.IsNullOrEmpty(existingBlobName))
        {
            await DeletePackageAsync(existingBlobName);
        }

        // Upload new package
        return await UploadPackageAsync(jobId, fileStream, originalFileName);
    }

    /// <summary>
    /// Deletes a package from Azure Blob Storage.
    /// </summary>
    /// <param name="blobName">The blob name to delete</param>
    public async Task DeletePackageAsync(string blobName)
    {
        if (string.IsNullOrEmpty(blobName))
            return;

        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();
    }

    /// <summary>
    /// Downloads a package to a local path.
    /// </summary>
    /// <param name="blobName">The blob name to download</param>
    /// <param name="destinationPath">The local path to save the file</param>
    /// <returns>True if download succeeded, false if blob doesn't exist</returns>
    public async Task<bool> DownloadPackageAsync(string blobName, string destinationPath)
    {
        if (string.IsNullOrEmpty(blobName))
            return false;

        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
            return false;

        // Ensure destination directory exists
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await blobClient.DownloadToAsync(destinationPath);
        return true;
    }

    /// <summary>
    /// Downloads a package to a stream.
    /// </summary>
    /// <param name="blobName">The blob name to download</param>
    /// <returns>The download stream, or null if blob doesn't exist</returns>
    public async Task<Stream?> DownloadPackageToStreamAsync(string blobName)
    {
        if (string.IsNullOrEmpty(blobName))
            return null;

        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
            return null;

        var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Gets a SAS URL for downloading a package (useful for external access).
    /// </summary>
    /// <param name="blobName">The blob name</param>
    /// <param name="expiresIn">How long the URL should be valid (default 1 hour)</param>
    /// <returns>The SAS URL, or null if blob doesn't exist</returns>
    public async Task<string?> GetPackageDownloadUriAsync(string blobName, TimeSpan? expiresIn = null)
    {
        if (string.IsNullOrEmpty(blobName))
            return null;

        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
            return null;

        // Check if we can generate SAS (requires account key or user delegation)
        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = ContainerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiresIn ?? TimeSpan.FromHours(1))
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        // Return the blob URI without SAS if we can't generate one
        return blobClient.Uri.ToString();
    }

    /// <summary>
    /// Checks if a package exists in blob storage.
    /// </summary>
    /// <param name="blobName">The blob name to check</param>
    /// <returns>True if the blob exists</returns>
    public async Task<bool> PackageExistsAsync(string blobName)
    {
        if (string.IsNullOrEmpty(blobName))
            return false;

        var blobClient = _containerClient.GetBlobClient(blobName);
        return await blobClient.ExistsAsync();
    }
}
