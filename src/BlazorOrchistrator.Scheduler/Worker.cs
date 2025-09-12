using BlazorOrchistrator.Scheduler.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorOrchistrator.Scheduler;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Scheduler running at: {time}", DateTimeOffset.Now);
                
                // Use scoped service for database operations
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
                _logger.LogInformation("Database context created successfully");
                
                // Add your scheduling logic here
                // For example: check for pending tasks, execute them, etc.
            }
            await Task.Delay(10000, stoppingToken); // Run every 10 seconds
        }
    }
}
