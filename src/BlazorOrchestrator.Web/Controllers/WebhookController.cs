using BlazorOrchestrator.Web.Services;
using BlazorDataOrchestrator.Core;
using Microsoft.AspNetCore.Mvc;

namespace BlazorOrchestrator.Web.Controllers;

/// <summary>
/// API controller for handling webhook requests to trigger job execution.
/// </summary>
[ApiController]
[Route("webhook")]
public class WebhookController : ControllerBase
{
    private readonly WebhookService _webhookService;
    private readonly JobManager _jobManager;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        WebhookService webhookService,
        JobManager jobManager,
        ILogger<WebhookController> logger)
    {
        _webhookService = webhookService;
        _jobManager = jobManager;
        _logger = logger;
    }

    /// <summary>
    /// Webhook endpoint to trigger a job execution.
    /// GET or POST /webhook/{guid}?webAPIParameter=value
    /// </summary>
    /// <param name="guid">The webhook GUID associated with the job</param>
    /// <returns>JSON response with job execution details</returns>
    [HttpGet("{guid}")]
    [HttpPost("{guid}")]
    public async Task<IActionResult> TriggerJob(string guid)
    {
        try
        {
            _logger.LogInformation("Webhook triggered for GUID: {WebhookGuid}", guid);

            // Validate GUID format
            if (string.IsNullOrWhiteSpace(guid) || !Guid.TryParse(guid, out _))
            {
                _logger.LogWarning("Invalid webhook GUID format: {WebhookGuid}", guid);
                return BadRequest(new { error = "Invalid webhook GUID format" });
            }

            // Get the job by webhook GUID
            var job = await _webhookService.GetJobByWebhookGuidAsync(guid);
            if (job == null)
            {
                _logger.LogWarning("Webhook GUID not found or job disabled: {WebhookGuid}", guid);
                return NotFound(new { error = "Webhook not found or job is disabled" });
            }

            // Capture query string parameters
            string? webhookParameters = null;
            if (Request.QueryString.HasValue)
            {
                webhookParameters = Request.QueryString.Value?.TrimStart('?');
            }

            // For POST requests, also capture body if present
            if (HttpContext.Request.Method == "POST" && Request.ContentLength > 0)
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrEmpty(body))
                {
                    webhookParameters = string.IsNullOrEmpty(webhookParameters)
                        ? $"__body__={Uri.EscapeDataString(body)}"
                        : $"{webhookParameters}&__body__={Uri.EscapeDataString(body)}";
                }
            }

            _logger.LogInformation("Running job {JobId} ({JobName}) via webhook with parameters: {Parameters}",
                job.Id, job.JobName, webhookParameters ?? "(none)");

            // Trigger job execution with webhook parameters
            var jobInstanceId = await _jobManager.RunJobNowWithWebhookAsync(job.Id, webhookParameters);

            _logger.LogInformation("Job {JobId} queued successfully via webhook. Instance ID: {JobInstanceId}",
                job.Id, jobInstanceId);

            return Ok(new
            {
                success = true,
                jobId = job.Id,
                jobName = job.JobName,
                jobInstanceId = jobInstanceId,
                message = $"Job '{job.JobName}' queued for execution",
                triggeredAt = DateTime.UtcNow
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation for webhook GUID: {WebhookGuid}", guid);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook for GUID: {WebhookGuid}", guid);
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    /// <summary>
    /// Health check endpoint to verify webhook is accessible.
    /// GET /webhook/health
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
