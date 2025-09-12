using BlazorOrchistrator.Agent;
using BlazorOrchistrator.Agent.Data;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.Services.AddHostedService<Worker>();

// Add Aspire integrations
builder.AddSqlServerDbContext<AgentDbContext>("database");
builder.AddAzureQueueServiceClient("queues");
builder.AddAzureBlobServiceClient("blobs");

var host = builder.Build();

host.Run();
