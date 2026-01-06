using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Define a secret parameter for SA
var saPassword = builder.AddParameter("sqlServer-password", secret: true);

// Pass that parameter into AddSqlServer
// Add a SQL Server container with fixed port
var sqlServer = builder.AddSqlServer("sqlServer", saPassword, port: 1433)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var db = sqlServer.AddDatabase("blazororchestratordb");

// Add Azurite storage emulator for development
// This will automatically start Azurite container or use local installation
var storage = builder.AddAzureStorage("storage");

if (builder.Environment.IsDevelopment())
{
    storage.RunAsEmulator(emulator =>
    {
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithEndpoint("blob", endpoint => endpoint.Port = 10000);
        emulator.WithEndpoint("queue", endpoint => endpoint.Port = 10001);
        emulator.WithEndpoint("table", endpoint => endpoint.Port = 10002);
    });
}

// Add Blob storage resource
var blobs = storage.AddBlobs("blobs");

// Add Table storage resource  
var tables = storage.AddTables("tables");

// Add Queue storage resource
var queues = storage.AddQueues("queues");

// Blazor Server Web App
var webApp = builder.AddProject<Projects.BlazorOrchestrator_Web>("webapp")
    .WithReference(db)
    .WithReference(blobs)
    .WithReference(tables)
    .WithReference(queues)
    .WaitFor(db);

// Scheduler service
var scheduler = builder.AddProject<Projects.BlazorOrchestrator_Scheduler>("scheduler")
    .WithReference(db)
    .WithReference(blobs)
    .WithReference(tables)
    .WithReference(queues)
    .WaitFor(db);

// Agent service - changed from container to project
var agent = builder.AddProject<Projects.BlazorOrchestrator_Agent>("agent")
    .WithReference(db)
    .WithReference(blobs)
    .WithReference(tables)
    .WithReference(queues)
    .WaitFor(db);

builder.Build().Run();