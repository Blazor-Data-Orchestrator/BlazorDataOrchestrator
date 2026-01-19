// This file has been added for environment-specific appsettings and container-based queueing
#nullable disable
using System;
using System.Collections.Generic;

namespace BlazorDataOrchestrator.Core.Data;

/// <summary>
/// Represents a job queue configuration for routing jobs to specific Azure Queues.
/// </summary>
public partial class JobQueue
{
    public int Id { get; set; }

    /// <summary>
    /// The name of the queue (e.g., "default", "jobs-large-container", "jobs-small-container").
    /// </summary>
    public string QueueName { get; set; }

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; }

    /// <summary>
    /// Jobs that are assigned to this queue.
    /// </summary>
    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
