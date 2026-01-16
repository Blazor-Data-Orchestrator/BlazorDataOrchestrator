using BlazorOrchestrator.Agent;
using BlazorOrchestrator.Agent.Data;
using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Services;
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

// Register Core services (JobStorageService, PackageProcessorService, CodeExecutorService, JobManager)
builder.Services.AddSingleton<JobStorageService>(sp =>
{
    var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
    return new JobStorageService(blobServiceClient);
});

builder.Services.AddSingleton<PackageProcessorService>(sp =>
{
    var storageService = sp.GetRequiredService<JobStorageService>();
    return new PackageProcessorService(storageService);
});

builder.Services.AddSingleton<CodeExecutorService>(sp =>
{
    var packageProcessor = sp.GetRequiredService<PackageProcessorService>();
    return new CodeExecutorService(packageProcessor);
});

builder.Services.AddSingleton<JobManager>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var sqlConnectionString = config.GetConnectionString("blazororchestratordb") ?? "";
    var blobConnectionString = config.GetConnectionString("blobs") ?? "";
    var queueConnectionString = config.GetConnectionString("queues") ?? "";
    var tableConnectionString = config.GetConnectionString("tables") ?? "";
    
    return new JobManager(sqlConnectionString, blobConnectionString, queueConnectionString, tableConnectionString);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();
