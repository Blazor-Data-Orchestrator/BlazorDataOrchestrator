using BlazorOrchistrator.Scheduler;
using BlazorOrchistrator.Scheduler.Data;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.Services.AddHostedService<Worker>();

// Add Aspire integrations - use the correct database name from AppHost
builder.AddSqlServerDbContext<SchedulerDbContext>("blazororchestratordb");

var host = builder.Build();

host.Run();
