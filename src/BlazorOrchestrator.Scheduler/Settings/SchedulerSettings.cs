namespace BlazorOrchestrator.Scheduler.Settings;

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
    /// Default queue name when job's EnvironmentQueue is null or empty.
    /// </summary>
    public string DefaultQueueName { get; set; } = "default-queue";

    /// <summary>
    /// Queue name for Azure-based jobs (when EnvironmentQueue equals "azure").
    /// </summary>
    public string AzureQueueContainer { get; set; } = "azure-queue";

    /// <summary>
    /// Queue name for on-premises jobs (when EnvironmentQueue equals "onprem").
    /// </summary>
    public string OnPremisesQueueContainer { get; set; } = "onprem-queue";

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
}
