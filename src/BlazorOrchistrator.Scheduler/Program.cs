using BlazorOrchistrator.Scheduler;
using BlazorOrchistrator.Scheduler.Data;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

// Add Aspire integrations (conditional based on environment)
if (builder.Environment.IsDevelopment())
{
    // For development without full Aspire orchestration, use in-memory databases
    builder.Services.AddDbContext<SchedulerDbContext>(options =>
        options.UseInMemoryDatabase("SchedulerDb"));
    
    // Note: Azure services would be configured when running with Aspire orchestration
    // builder.AddSqlServerDbContext<SchedulerDbContext>("database");
    // builder.AddAzureQueueClient("queues");
    // builder.AddAzureBlobClient("blobs");
}
else
{
    // Production or Aspire orchestration
    builder.AddSqlServerDbContext<SchedulerDbContext>("database");
    builder.AddAzureQueueClient("queues");
    builder.AddAzureBlobClient("blobs");
}

var host = builder.Build();
host.Run();
