using BlazorOrchestrator.Scheduler;
using BlazorOrchestrator.Scheduler.Data;
using BlazorOrchestrator.Scheduler.Services;
using BlazorOrchestrator.Scheduler.Settings;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add Azure clients
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");
builder.AddAzureQueueServiceClient("queues");

// Configure SchedulerSettings from appsettings.json
builder.Services.Configure<SchedulerSettings>(
    builder.Configuration.GetSection("SchedulerSettings"));

// Register services
builder.Services.AddScoped<IJobQueueService, JobQueueService>();

builder.Services.AddHostedService<Worker>();

// Add Aspire integrations - use the correct database name from AppHost
builder.AddSqlServerDbContext<SchedulerDbContext>("blazororchestratordb");

var host = builder.Build();

host.Run();
