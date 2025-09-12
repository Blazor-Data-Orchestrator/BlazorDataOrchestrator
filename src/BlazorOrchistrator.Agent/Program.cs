using BlazorOrchistrator.Agent;
using BlazorOrchistrator.Agent.Data;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

// Add Aspire integrations
builder.AddSqlServerDbContext<AgentDbContext>("database");
builder.AddAzureQueueClient("queues");
builder.AddAzureBlobClient("blobs");

var host = builder.Build();
host.Run();
