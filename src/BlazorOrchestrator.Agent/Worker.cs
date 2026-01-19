using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using BlazorOrchestrator.Agent.Data;
using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorOrchestrator.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly QueueServiceClient? _queueServiceClient;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly JobManager _jobManager;
    private readonly PackageProcessorService _packageProcessor;
    private readonly CodeExecutorService _codeExecutor;
    private readonly string _agentId;

    public Worker(
        ILogger<Worker> logger, 
        IServiceProvider serviceProvider,
        JobManager jobManager,
        PackageProcessorService packageProcessor,
        CodeExecutorService codeExecutor)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _jobManager = jobManager;
        _packageProcessor = packageProcessor;
        _codeExecutor = codeExecutor;
        _agentId = $"Agent-{Environment.MachineName}-{Guid.NewGuid():N}".Substring(0, 50);
        
        // Optionally resolve Azure services if available
        _queueServiceClient = serviceProvider.GetService<QueueServiceClient>();
        _blobServiceClient = serviceProvider.GetService<BlobServiceClient>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent {AgentId} starting...", _agentId);

        // Get queue client
        if (_queueServiceClient == null)
        {
            _logger.LogWarning("Queue service not configured. Agent will run in polling mode without processing jobs.");
            await RunPollingModeAsync(stoppingToken);
            return;
        }

        var queueClient = _queueServiceClient.GetQueueClient("default");
        await queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        _logger.LogInformation("Agent {AgentId} connected to queue: {QueueUri}", _agentId, queueClient.Uri);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Receive message from queue
                var response = await queueClient.ReceiveMessageAsync(
                    visibilityTimeout: TimeSpan.FromMinutes(5), // Hide message for 5 minutes while processing
                    cancellationToken: stoppingToken);

                if (response?.Value == null)
                {
                    // No messages, wait and try again
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                var message = response.Value;
                _logger.LogInformation("Received message: {MessageId}", message.MessageId);

                try
                {
                    // Parse the queue message
                    var messageBody = Encoding.UTF8.GetString(Convert.FromBase64String(message.Body.ToString()));
                    var queueMessage = JsonSerializer.Deserialize<JobQueueMessage>(messageBody);

                    if (queueMessage == null)
                    {
                        _logger.LogWarning("Failed to deserialize queue message. Deleting invalid message.");
                        await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Processing JobInstance {JobInstanceId} for Job {JobId}", 
                        queueMessage.JobInstanceId, queueMessage.JobId);

                    // Delegate all processing to Core's JobManager
                    await _jobManager.ProcessJobInstanceAsync(
                        queueMessage.JobInstanceId,
                        _packageProcessor,
                        _codeExecutor,
                        _agentId);

                    _logger.LogInformation("Successfully processed JobInstance {JobInstanceId}", queueMessage.JobInstanceId);

                    // Delete message from queue after successful processing
                    await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
                    
                    // Message will become visible again after visibility timeout
                    // After too many failures, it will go to poison queue (if configured)
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in agent worker loop");
                await Task.Delay(10000, stoppingToken); // Wait before retrying
            }
        }

        _logger.LogInformation("Agent {AgentId} shutting down.", _agentId);
    }

    /// <summary>
    /// Fallback mode when queue service is not configured - just logs status periodically.
    /// </summary>
    private async Task RunPollingModeAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Agent running at: {time}", DateTimeOffset.Now);
                
                if (_queueServiceClient != null)
                    _logger.LogInformation("Queue service endpoint: {endpoint}", _queueServiceClient.Uri);
                else
                    _logger.LogInformation("Queue service: Not configured (development mode)");
                    
                if (_blobServiceClient != null)
                    _logger.LogInformation("Blob service endpoint: {endpoint}", _blobServiceClient.Uri);
                else
                    _logger.LogInformation("Blob service: Not configured (development mode)");
                
                // Use scoped service for database operations
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                _logger.LogInformation("Database context created successfully");
            }
            await Task.Delay(15000, stoppingToken); // Run every 15 seconds
        }
    }
}
