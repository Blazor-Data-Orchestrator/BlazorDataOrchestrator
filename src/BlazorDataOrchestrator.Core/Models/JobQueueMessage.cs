namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Represents a message sent to the job queue for processing by the Agent.
/// </summary>
public class JobQueueMessage
{
    /// <summary>
    /// The ID of the job instance to process.
    /// </summary>
    public int JobInstanceId { get; set; }

    /// <summary>
    /// The ID of the job (for quick reference without DB lookup).
    /// </summary>
    public int JobId { get; set; }

    /// <summary>
    /// The timestamp when the message was queued.
    /// </summary>
    public DateTime QueuedAt { get; set; }
}
