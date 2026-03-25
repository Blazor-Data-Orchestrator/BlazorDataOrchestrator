using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorOrchestrator.Web.Controllers;

/// <summary>
/// API controller for accessing build errors and LLM fix attempt metrics.
/// Provides the /api/build-errors/* endpoints consumed by the UI and the LLM prompt builder.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/build-errors")]
public class BuildErrorsController : ControllerBase
{
    private readonly BuildTelemetryReader _telemetryReader;
    private readonly FixAttemptStore _attemptStore;
    private readonly ILogger<BuildErrorsController> _logger;

    public BuildErrorsController(
        BuildTelemetryReader telemetryReader,
        FixAttemptStore attemptStore,
        ILogger<BuildErrorsController> logger)
    {
        _telemetryReader = telemetryReader;
        _attemptStore = attemptStore;
        _logger = logger;
    }

    /// <summary>
    /// Gets the latest build errors, optionally filtered by project.
    /// </summary>
    /// <param name="count">Number of errors to return (default 20).</param>
    /// <param name="project">Optional project name filter.</param>
    [HttpGet("latest")]
    public ActionResult<IReadOnlyList<BuildError>> GetLatest(
        [FromQuery] int count = 20,
        [FromQuery] string? project = null)
    {
        var errors = _telemetryReader.GetLatestErrors(count, project);
        return Ok(errors);
    }

    /// <summary>
    /// Gets aggregated metrics from the LLM fix attempt store.
    /// </summary>
    [HttpGet("metrics")]
    public ActionResult<FixAttemptMetrics> GetMetrics()
    {
        var metrics = _attemptStore.GetMetrics();
        return Ok(metrics);
    }

    /// <summary>
    /// Gets recent fix attempts, optionally filtered by error code.
    /// </summary>
    /// <param name="errorCode">Optional error code filter (e.g. "CS1061").</param>
    /// <param name="limit">Maximum number of attempts to return.</param>
    [HttpGet("fix-attempts")]
    public ActionResult<IReadOnlyList<FixAttemptSummary>> GetFixAttempts(
        [FromQuery] string? errorCode = null,
        [FromQuery] int limit = 50)
    {
        var attempts = errorCode != null
            ? _attemptStore.GetByErrorCode(errorCode)
            : _attemptStore.GetAll(limit);

        // Return summaries (exclude full prompt/response to keep payloads manageable)
        var summaries = attempts.Select(a => new FixAttemptSummary
        {
            Id = a.Id,
            ErrorCode = a.OriginalError.ErrorCode,
            ErrorMessage = a.OriginalError.Message,
            FilePath = a.OriginalError.FilePath,
            Line = a.OriginalError.Line,
            RebuildSucceeded = a.RebuildSucceeded,
            RootCauseCategory = a.RootCauseCategory.ToString(),
            ResidualErrorCode = a.ResidualError?.ErrorCode,
            ResidualErrorMessage = a.ResidualError?.Message,
            Timestamp = a.Timestamp
        }).ToList();

        return Ok(summaries);
    }

    /// <summary>
    /// Gets negative examples for a specific error code.
    /// </summary>
    [HttpGet("negative-examples/{errorCode}")]
    public ActionResult<IReadOnlyList<NegativeExample>> GetNegativeExamples(string errorCode)
    {
        var examples = _attemptStore.GetNegativeExamples(errorCode);
        return Ok(examples);
    }
}

/// <summary>
/// Lightweight summary of a fix attempt for API responses.
/// Excludes the full prompt and LLM response to keep payloads small.
/// </summary>
public class FixAttemptSummary
{
    public Guid Id { get; init; }
    public string ErrorCode { get; init; } = "";
    public string ErrorMessage { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
    public bool RebuildSucceeded { get; init; }
    public string RootCauseCategory { get; init; } = "";
    public string? ResidualErrorCode { get; init; }
    public string? ResidualErrorMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
