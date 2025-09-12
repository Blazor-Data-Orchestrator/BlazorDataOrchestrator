using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using BlazorOrchistrator.Agent.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorOrchistrator.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly QueueServiceClient _queueServiceClient;
    private readonly BlobServiceClient _blobServiceClient;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider,
                  QueueServiceClient queueServiceClient, BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _queueServiceClient = queueServiceClient;
        _blobServiceClient = blobServiceClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Agent running at: {time}", DateTimeOffset.Now);
                _logger.LogInformation("Queue service endpoint: {endpoint}", _queueServiceClient?.Uri);
                _logger.LogInformation("Blob service endpoint: {endpoint}", _blobServiceClient?.Uri);
                
                // Use scoped service for database operations
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                _logger.LogInformation("Database context created successfully");
            }
            await Task.Delay(15000, stoppingToken); // Run every 15 seconds
        }
    }
}
