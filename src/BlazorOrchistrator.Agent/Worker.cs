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

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        
        // Optionally resolve Azure services if available
        _queueServiceClient = serviceProvider.GetService<QueueServiceClient>();
        _blobServiceClient = serviceProvider.GetService<BlobServiceClient>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
