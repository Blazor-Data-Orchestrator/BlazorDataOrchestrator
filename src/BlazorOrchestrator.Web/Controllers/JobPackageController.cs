using BlazorDataOrchestrator.Core;
using Microsoft.AspNetCore.Mvc;

namespace BlazorOrchestrator.Web.Controllers;

/// <summary>
/// API controller for handling job package downloads.
/// </summary>
[ApiController]
[Route("api/job-package")]
public class JobPackageController : ControllerBase
{
    private readonly JobManager _jobManager;
    private readonly ILogger<JobPackageController> _logger;

    public JobPackageController(
        JobManager jobManager,
        ILogger<JobPackageController> logger)
    {
        _jobManager = jobManager;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the code package for a specific job.
    /// GET /api/job-package/{jobId}/download
    /// </summary>
    /// <param name="jobId">The job ID</param>
    /// <returns>The .nupkg file as a download</returns>
    [HttpGet("{jobId:int}/download")]
    public async Task<IActionResult> DownloadPackage(int jobId)
    {
        try
        {
            _logger.LogInformation("Download requested for job package: {JobId}", jobId);

            // Get the job code file name for the Content-Disposition header
            var fileName = await _jobManager.GetJobCodeFileAsync(jobId);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("No code package found for job: {JobId}", jobId);
                return NotFound(new { error = "No code package found for this job" });
            }

            // Download the package stream
            var packageStream = await _jobManager.DownloadJobPackageAsync(jobId);
            if (packageStream == null)
            {
                _logger.LogWarning("Failed to download package for job: {JobId}", jobId);
                return NotFound(new { error = "Package not found in storage" });
            }

            _logger.LogInformation("Successfully retrieved package for job: {JobId}, FileName: {FileName}", jobId, fileName);
            
            return File(packageStream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading package for job: {JobId}", jobId);
            return StatusCode(500, new { error = "An error occurred while downloading the package" });
        }
    }
}
