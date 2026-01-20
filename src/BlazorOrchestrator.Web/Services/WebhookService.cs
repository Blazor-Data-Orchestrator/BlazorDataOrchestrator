using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for managing webhook operations on jobs.
/// </summary>
public class WebhookService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(ApplicationDbContext dbContext, ILogger<WebhookService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Enables webhook for a job by generating a unique GUID.
    /// </summary>
    /// <param name="jobId">The job ID to enable webhook for</param>
    /// <returns>The generated webhook GUID</returns>
    public async Task<string> EnableWebhookAsync(int jobId)
    {
        var job = await _dbContext.Jobs.FindAsync(jobId);
        if (job == null)
            throw new InvalidOperationException($"Job {jobId} not found");

        job.WebhookGuid = Guid.NewGuid().ToString();
        job.UpdatedDate = DateTime.UtcNow;
        job.UpdatedBy = "System";
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Enabled webhook for job {JobId} with GUID {WebhookGuid}", jobId, job.WebhookGuid);
        return job.WebhookGuid;
    }

    /// <summary>
    /// Disables webhook for a job by clearing the GUID.
    /// </summary>
    /// <param name="jobId">The job ID to disable webhook for</param>
    public async Task DisableWebhookAsync(int jobId)
    {
        var job = await _dbContext.Jobs.FindAsync(jobId);
        if (job == null)
            throw new InvalidOperationException($"Job {jobId} not found");

        var oldGuid = job.WebhookGuid;
        job.WebhookGuid = null;
        job.UpdatedDate = DateTime.UtcNow;
        job.UpdatedBy = "System";
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Disabled webhook for job {JobId} (removed GUID {WebhookGuid})", jobId, oldGuid);
    }

    /// <summary>
    /// Gets a job by its webhook GUID.
    /// </summary>
    /// <param name="webhookGuid">The webhook GUID to lookup</param>
    /// <returns>The job if found and enabled, null otherwise</returns>
    public async Task<Job?> GetJobByWebhookGuidAsync(string webhookGuid)
    {
        return await _dbContext.Jobs
            .Include(j => j.JobSchedules)
            .Include(j => j.JobQueueNavigation)
            .FirstOrDefaultAsync(j => j.WebhookGuid == webhookGuid && j.JobEnabled);
    }

    /// <summary>
    /// Checks if a webhook GUID exists and is valid.
    /// </summary>
    /// <param name="webhookGuid">The webhook GUID to check</param>
    /// <returns>True if the webhook exists and the job is enabled</returns>
    public async Task<bool> IsWebhookValidAsync(string webhookGuid)
    {
        return await _dbContext.Jobs
            .AnyAsync(j => j.WebhookGuid == webhookGuid && j.JobEnabled);
    }
}
