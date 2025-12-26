using BlazorOrchestrator.Scheduler;
using BlazorOrchestrator.Scheduler.Data;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add Azure clients
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");
builder.AddAzureQueueServiceClient("queues");

builder.Services.AddHostedService<Worker>();

// Add Aspire integrations - use the correct database name from AppHost
builder.AddSqlServerDbContext<SchedulerDbContext>("blazororchestratordb");

var host = builder.Build();

host.Run();
