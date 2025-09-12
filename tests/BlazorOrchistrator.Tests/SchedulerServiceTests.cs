using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using BlazorOrchistrator.Scheduler;
using BlazorOrchistrator.Scheduler.Data;

namespace BlazorOrchistrator.Tests;

public class SchedulerServiceTests
{
    [Fact]
    public void SchedulerService_CanBeCreated()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SchedulerDbContext>(options =>
            options.UseInMemoryDatabase("TestSchedulerDb"));
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Worker>>();

        // Act
        var worker = new Worker(logger, serviceProvider);

        // Assert
        Assert.NotNull(worker);
    }

    [Fact]
    public void SchedulerDbContext_CanBeCreated()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb")
            .Options;

        // Act & Assert
        using var context = new SchedulerDbContext(options);
        Assert.NotNull(context);
    }
}