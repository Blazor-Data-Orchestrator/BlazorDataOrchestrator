using System.Text.Json;

namespace BlazorOrchestrator.Scheduler.Messages;

/// <summary>
/// Message contract for job queue messages.
/// This format is shared with the Agent project for deserialization.
/// </summary>
public class JobQueueMessage
{
    /// <summary>
    /// The ID of the job instance being scheduled.
    /// </summary>
    public int JobInstanceId { get; set; }

    /// <summary>
    /// The ID of the job.
    /// </summary>
    public int JobId { get; set; }

    /// <summary>
    /// The name of the queue this message was sent to.
    /// </summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// The UTC timestamp when the job was scheduled.
    /// </summary>
    public DateTime ScheduledAtUtc { get; set; }

    /// <summary>
    /// Optional metadata for the job execution.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Serializes the message to JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Deserializes a JSON string to a JobQueueMessage.
    /// </summary>
    public static JobQueueMessage? FromJson(string json)
    {
        return JsonSerializer.Deserialize<JobQueueMessage>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
