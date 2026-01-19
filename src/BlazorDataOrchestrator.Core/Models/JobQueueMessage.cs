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

    /// <summary>
    /// The environment the job should run in (e.g., Production, Staging, Development).
    /// Used to determine which appsettings file to load from the NuGet package.
    /// </summary>
    public string? JobEnvironment { get; set; }

    /// <summary>
    /// The name of the queue this message was sent to.
    /// Used by agents to verify they are processing the correct queue.
    /// </summary>
    public string? JobQueueName { get; set; }
}
