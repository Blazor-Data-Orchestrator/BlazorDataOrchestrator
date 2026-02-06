using BlazorOrchestrator.Scheduler.Messages;

namespace BlazorOrchestrator.Scheduler.Services;

/// <summary>
/// Service for managing job queue operations.
/// </summary>
public interface IJobQueueService
{
    /// <summary>
    /// Enqueues a job instance to the appropriate Azure Storage Queue.
    /// </summary>
    /// <param name="jobInstanceId">The ID of the job instance.</param>
    /// <param name="jobId">The ID of the job.</param>
    /// <param name="queueName">The resolved queue name.</param>
    /// <returns>True if the message was successfully enqueued, false otherwise.</returns>
    Task<bool> EnqueueJobAsync(int jobInstanceId, int jobId, string queueName);
}
