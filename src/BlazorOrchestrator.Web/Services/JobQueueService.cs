using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for managing JobQueue entities.
/// </summary>
public class JobQueueService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<JobQueueService> _logger;

    public JobQueueService(ApplicationDbContext dbContext, ILogger<JobQueueService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets all job queues with their associated jobs.
    /// </summary>
    public async Task<List<JobQueue>> GetAllJobQueuesAsync()
    {
        return await _dbContext.JobQueues
            .Include(q => q.Jobs)
            .OrderBy(q => q.QueueName)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all active job queues for dropdown selection.
    /// </summary>
    public async Task<List<JobQueue>> GetActiveJobQueuesAsync()
    {
        return await _dbContext.JobQueues
            .OrderBy(q => q.QueueName)
            .ToListAsync();
    }

    /// <summary>
    /// Gets a job queue by ID.
    /// </summary>
    public async Task<JobQueue?> GetJobQueueByIdAsync(int id)
    {
        return await _dbContext.JobQueues
            .Include(q => q.Jobs)
            .FirstOrDefaultAsync(q => q.Id == id);
    }

    /// <summary>
    /// Gets a job queue by name.
    /// </summary>
    public async Task<JobQueue?> GetJobQueueByNameAsync(string queueName)
    {
        return await _dbContext.JobQueues
            .FirstOrDefaultAsync(q => q.QueueName == queueName);
    }

    /// <summary>
    /// Creates a new job queue.
    /// </summary>
    public async Task<JobQueue> CreateJobQueueAsync(JobQueue queue)
    {
        // Check for duplicate queue name
        var existing = await _dbContext.JobQueues
            .FirstOrDefaultAsync(q => q.QueueName == queue.QueueName);
        
        if (existing != null)
        {
            throw new InvalidOperationException($"A job queue with the name '{queue.QueueName}' already exists.");
        }

        queue.CreatedDate = DateTime.UtcNow;
        queue.CreatedBy = "System";

        _dbContext.JobQueues.Add(queue);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created job queue '{QueueName}' with ID {QueueId}", queue.QueueName, queue.Id);
        return queue;
    }

    /// <summary>
    /// Updates an existing job queue.
    /// </summary>
    public async Task<JobQueue> UpdateJobQueueAsync(JobQueue queue)
    {
        // Check for duplicate queue name (excluding current queue)
        var existing = await _dbContext.JobQueues
            .FirstOrDefaultAsync(q => q.QueueName == queue.QueueName && q.Id != queue.Id);
        
        if (existing != null)
        {
            throw new InvalidOperationException($"A job queue with the name '{queue.QueueName}' already exists.");
        }

        _dbContext.JobQueues.Update(queue);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Updated job queue '{QueueName}' with ID {QueueId}", queue.QueueName, queue.Id);
        return queue;
    }

    /// <summary>
    /// Deletes a job queue by ID.
    /// </summary>
    public async Task DeleteJobQueueAsync(int id)
    {
        var queue = await _dbContext.JobQueues
            .Include(q => q.Jobs)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (queue == null)
        {
            throw new ArgumentException($"Job queue with ID {id} not found.");
        }

        if (queue.Jobs.Count > 0)
        {
            throw new InvalidOperationException($"Cannot delete job queue '{queue.QueueName}' because it has {queue.Jobs.Count} job(s) assigned.");
        }

        _dbContext.JobQueues.Remove(queue);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted job queue '{QueueName}' with ID {QueueId}", queue.QueueName, id);
    }
}
