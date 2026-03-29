namespace BlazorOrchestrator.Scheduler.Settings;

/// <summary>
/// Constant values for queue names - hardcoded to prevent accidental modification.
/// </summary>
public static class QueueConstants
{
    /// <summary>
    /// Default queue name when job's queue is null or empty.
    /// </summary>
    public const string DefaultQueueName = "default";

    /// <summary>
    /// Queue name for Azure-based jobs.
    /// </summary>
    public const string AzureQueueContainer = "azure-queue";

    /// <summary>
    /// Queue name for on-premises jobs.
    /// </summary>
    public const string OnPremisesQueueContainer = "onprem-queue";
}

/// <summary>
/// Configuration settings for the Scheduler service.
/// </summary>
public class SchedulerSettings
{
    /// <summary>
    /// The polling interval in seconds between scheduler runs.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Enable verbose logging for debugging purposes.
    /// </summary>
    public bool VerboseLogging { get; set; } = false;

    /// <summary>
    /// Number of hours after which a job instance without UpdatedDate is marked as stuck/error.
    /// </summary>
    public int StuckJobTimeoutHours { get; set; } = 24;

    /// <summary>
    /// Number of retry attempts for queue operations.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Delay in seconds between retry attempts.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// IANA timezone ID (e.g., "America/Los_Angeles") used for schedule evaluation.
    /// This is a fallback; the primary source is the "TimezoneId" setting in Azure Table Storage.
    /// </summary>
    public string TimezoneId { get; set; } = "America/Los_Angeles";
}
