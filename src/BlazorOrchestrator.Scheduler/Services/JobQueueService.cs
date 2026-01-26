using Azure.Storage.Queues;
using BlazorOrchestrator.Scheduler.Messages;
using BlazorOrchestrator.Scheduler.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorOrchestrator.Scheduler.Services;

/// <summary>
/// Implementation of IJobQueueService using Azure Storage Queues.
/// </summary>
public class JobQueueService : IJobQueueService
{
    private readonly QueueServiceClient _queueServiceClient;
    private readonly SchedulerSettings _settings;
    private readonly ILogger<JobQueueService> _logger;

    public JobQueueService(
        QueueServiceClient queueServiceClient,
        IOptions<SchedulerSettings> settings,
        ILogger<JobQueueService> logger)
    {
        _queueServiceClient = queueServiceClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> EnqueueJobAsync(int jobInstanceId, int jobId, string queueName)
    {
        var message = new JobQueueMessage
        {
            JobInstanceId = jobInstanceId,
            JobId = jobId,
            QueueName = queueName,
            ScheduledAtUtc = DateTime.UtcNow
        };

        var messageJson = message.ToJson();

        // Retry logic
        for (int attempt = 1; attempt <= _settings.RetryCount; attempt++)
        {
            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();
                await queueClient.SendMessageAsync(messageJson);

                if (_settings.VerboseLogging)
                {
                    _logger.LogInformation(
                        "Successfully enqueued JobInstance {JobInstanceId} to queue '{QueueName}' (attempt {Attempt})",
                        jobInstanceId, queueName, attempt);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to enqueue JobInstance {JobInstanceId} to queue '{QueueName}' (attempt {Attempt}/{MaxAttempts})",
                    jobInstanceId, queueName, attempt, _settings.RetryCount);

                if (attempt < _settings.RetryCount)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.RetryDelaySeconds));
                }
                else
                {
                    _logger.LogError(ex,
                        "All retry attempts exhausted for JobInstance {JobInstanceId}. Message not enqueued.",
                        jobInstanceId);
                    return false;
                }
            }
        }

        return false;
    }
}
