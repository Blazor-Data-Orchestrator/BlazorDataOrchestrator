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
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly QueueServiceClient? _queueServiceClient;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly JobManager _jobManager;
    private readonly PackageProcessorService _packageProcessor;
    private readonly CodeExecutorService _codeExecutor;
    private readonly string _agentId;
    private readonly string _queueName;
    
    // Visibility timeout configuration for long-running jobs
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(3);

    public Worker(
        ILogger<Worker> logger, 
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        JobManager jobManager,
        PackageProcessorService packageProcessor,
        CodeExecutorService codeExecutor)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _jobManager = jobManager;
        _packageProcessor = packageProcessor;
        _codeExecutor = codeExecutor;
        _agentId = $"Agent-{Environment.MachineName}-{Guid.NewGuid():N}".Substring(0, 50);
        
        // Get queue name from configuration, default to "default"
        _queueName = _configuration.GetValue<string>("QueueName") ?? "default";
        
        // Optionally resolve Azure services if available
        _queueServiceClient = serviceProvider.GetService<QueueServiceClient>();
        _blobServiceClient = serviceProvider.GetService<BlobServiceClient>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent {AgentId} starting...", _agentId);
        _logger.LogInformation("Agent configured to monitor queue: {QueueName}", _queueName);

        // Get queue client
        if (_queueServiceClient == null)
        {
            _logger.LogWarning("Queue service not configured. Agent will run in polling mode without processing jobs.");
            await RunPollingModeAsync(stoppingToken);
            return;
        }

        var queueClient = _queueServiceClient.GetQueueClient(_queueName);
        await queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        _logger.LogInformation("Agent {AgentId} connected to queue '{QueueName}': {QueueUri}", _agentId, _queueName, queueClient.Uri);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Receive message from queue
                var response = await queueClient.ReceiveMessageAsync(
                    visibilityTimeout: VisibilityTimeout,
                    cancellationToken: stoppingToken);

                if (response?.Value == null)
                {
                    // No messages, wait and try again
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                var message = response.Value;
                _logger.LogInformation("Received message: {MessageId} from queue '{QueueName}'", message.MessageId, _queueName);

                // Track the current pop receipt (it changes with each visibility update)
                var currentPopReceipt = message.PopReceipt;
                
                // Create a cancellation token source for the visibility renewal task
                using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                Task<string>? renewalTask = null;

                try
                {
                    // Parse the queue message
                    var messageBody = Encoding.UTF8.GetString(Convert.FromBase64String(message.Body.ToString()));
                    var queueMessage = JsonSerializer.Deserialize<JobQueueMessage>(messageBody);

                    if (queueMessage == null)
                    {
                        _logger.LogWarning("Failed to deserialize queue message. Deleting invalid message.");
                        await queueClient.DeleteMessageAsync(message.MessageId, currentPopReceipt, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Processing JobInstance {JobInstanceId} for Job {JobId} (Environment: {Environment}, Queue: {QueueName})", 
                        queueMessage.JobInstanceId, queueMessage.JobId, 
                        queueMessage.JobEnvironment ?? "N/A", queueMessage.JobQueueName ?? _queueName);

                    // Log webhook parameters if present
                    if (!string.IsNullOrEmpty(queueMessage.WebhookParameters))
                    {
                        _logger.LogInformation("Job triggered via webhook with parameters: {WebhookParameters}", 
                            queueMessage.WebhookParameters);
                    }

                    // Start the visibility renewal task to keep the message hidden during long-running jobs
                    renewalTask = RenewMessageVisibilityAsync(
                        queueClient, 
                        message.MessageId, 
                        currentPopReceipt, 
                        renewalCts.Token);

                    // Delegate all processing to Core's JobManager, passing the environment and webhook parameters from the queue message
                    await _jobManager.ProcessJobInstanceAsync(
                        queueMessage.JobInstanceId,
                        _packageProcessor,
                        _codeExecutor,
                        _agentId,
                        queueMessage.JobEnvironment,
                        queueMessage.WebhookParameters);

                    _logger.LogInformation("Successfully processed JobInstance {JobInstanceId}", queueMessage.JobInstanceId);

                    // Cancel the renewal task and get the latest pop receipt
                    await renewalCts.CancelAsync();
                    try
                    {
                        currentPopReceipt = await renewalTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when we cancel the renewal task
                    }

                    // Delete message from queue after successful processing using the latest pop receipt
                    await queueClient.DeleteMessageAsync(message.MessageId, currentPopReceipt, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
                    
                    // Cancel the renewal task on error
                    await renewalCts.CancelAsync();
                    if (renewalTask != null)
                    {
                        try
                        {
                            await renewalTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when we cancel the renewal task
                        }
                    }
                    
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
    /// Periodically renews the visibility timeout of a message to prevent it from becoming visible
    /// while a long-running job is being processed.
    /// </summary>
    /// <param name="queueClient">The queue client to use for updating the message.</param>
    /// <param name="messageId">The ID of the message to renew.</param>
    /// <param name="initialPopReceipt">The initial pop receipt of the message.</param>
    /// <param name="cancellationToken">Token to signal when renewal should stop.</param>
    /// <returns>The latest pop receipt after all renewals.</returns>
    private async Task<string> RenewMessageVisibilityAsync(
        QueueClient queueClient,
        string messageId,
        string initialPopReceipt,
        CancellationToken cancellationToken)
    {
        var currentPopReceipt = initialPopReceipt;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wait for the renewal interval before extending visibility
                await Task.Delay(RenewalInterval, cancellationToken);
                
                // Extend the visibility timeout
                var response = await queueClient.UpdateMessageAsync(
                    messageId,
                    currentPopReceipt,
                    visibilityTimeout: VisibilityTimeout,
                    cancellationToken: cancellationToken);
                
                // Update the pop receipt (it changes with each update)
                currentPopReceipt = response.Value.PopReceipt;
                
                _logger.LogDebug(
                    "Renewed visibility timeout for message {MessageId}, new timeout: {Timeout} minutes",
                    messageId, 
                    VisibilityTimeout.TotalMinutes);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal cancellation, return the latest pop receipt
            _logger.LogDebug("Visibility renewal cancelled for message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            // Log the error but don't throw - the job processing should continue
            _logger.LogWarning(
                ex, 
                "Failed to renew visibility timeout for message {MessageId}. The message may become visible to other agents.",
                messageId);
        }
        
        return currentPopReceipt;
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
                _logger.LogInformation("Agent configured for queue: {QueueName}", _queueName);
                
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
