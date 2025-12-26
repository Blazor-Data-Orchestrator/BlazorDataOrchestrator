using BlazorOrchestrator.Agent;
using BlazorOrchestrator.Agent.Data;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Queues;
using Azure.Storage.Blobs;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add Aspire integrations - use the correct database name from AppHost
builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("blazororchestratordb")));

// Add Azure clients
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");
builder.AddAzureQueueServiceClient("queues");

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();
