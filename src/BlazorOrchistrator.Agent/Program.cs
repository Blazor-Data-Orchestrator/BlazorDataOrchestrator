using BlazorOrchistrator.Agent;
using BlazorOrchistrator.Agent.Data;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

// Add Aspire integrations (conditional based on environment)
if (builder.Environment.IsDevelopment())
{
    // For development without full Aspire orchestration, use in-memory databases
    builder.Services.AddDbContext<AgentDbContext>(options =>
        options.UseInMemoryDatabase("AgentDb"));
    
    // Note: Azure services would be configured when running with Aspire orchestration
    // builder.AddSqlServerDbContext<AgentDbContext>("database");
    // builder.AddAzureQueueServiceClient("queues");
    // builder.AddAzureBlobServiceClient("blobs");
}
else
{
    // Production or Aspire orchestration
    builder.AddSqlServerDbContext<AgentDbContext>("database");
    builder.AddAzureQueueServiceClient("queues");
    builder.AddAzureBlobServiceClient("blobs");
}

var host = builder.Build();
host.Run();
