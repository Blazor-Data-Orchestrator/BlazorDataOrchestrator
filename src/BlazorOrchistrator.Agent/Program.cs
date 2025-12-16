using BlazorOrchistrator.Agent;
using BlazorOrchistrator.Agent.Data;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Queues;
using Azure.Storage.Blobs;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add Aspire integrations - use the correct database name from AppHost
builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("blazororchestratordb")));

builder.Services.AddSingleton<QueueServiceClient>(provider =>
{
    var connectionString = builder.Configuration["AZURE_STORAGE_CONNECTION_STRING"];
    if (string.IsNullOrEmpty(connectionString))
    {
        connectionString = builder.Configuration.GetConnectionString("AZURE_STORAGE_CONNECTION_STRING");
    }

    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Connection string 'AZURE_STORAGE_CONNECTION_STRING' not found.");
    }

    // Clean up the connection string to remove any potential wrapping quotes
    connectionString = connectionString.Trim().Trim('"');

    return new QueueServiceClient(connectionString);
});

builder.Services.AddSingleton<BlobServiceClient>(provider =>
{
    var connectionString = builder.Configuration["AZURE_STORAGE_CONNECTION_STRING"];
    if (string.IsNullOrEmpty(connectionString))
    {
        connectionString = builder.Configuration.GetConnectionString("AZURE_STORAGE_CONNECTION_STRING");
    }

    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Connection string 'AZURE_STORAGE_CONNECTION_STRING' not found.");
    }

    // Clean up the connection string to remove any potential wrapping quotes
    connectionString = connectionString.Trim().Trim('"');

    return new BlobServiceClient(connectionString);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();
