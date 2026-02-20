using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Database configuration:
// - Local dev: SQL Server container via RunAsContainer()
// - Azure deployment (azd up): Managed Azure SQL Database is provisioned automatically.
//   Users can change the database later via the app's built-in Install Wizard.
var sqlServer = builder.AddAzureSqlServer("sqlserver")
    .RunAsContainer(container =>
    {
        container.WithEnvironment("ACCEPT_EULA", "Y");
        container.WithDataVolume();
        container.WithLifetime(ContainerLifetime.Persistent);
    });

var db = sqlServer.AddDatabase("blazororchestratordb");

// Storage configuration:
// - Local dev: Azurite emulator via RunAsEmulator()
// - Azure deployment (azd up): Azure Storage Account is provisioned automatically.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithDataVolume();  // Persist Azurite data across restarts
        emulator.WithEndpoint("blob", endpoint => endpoint.Port = 10000);
        emulator.WithEndpoint("queue", endpoint => endpoint.Port = 10001);
        emulator.WithEndpoint("table", endpoint => endpoint.Port = 10002);
    });

var blobs = storage.AddBlobs("blobs");
var tables = storage.AddTables("tables");
var queues = storage.AddQueues("queues");

// Blazor Server Web App — WithExternalHttpEndpoints for public ingress in ACA
var webApp = builder.AddProject<Projects.BlazorOrchestrator_Web>("webapp")
    .WithExternalHttpEndpoints()
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WithReference(tables).WithReference(queues);

// Scheduler service
var scheduler = builder.AddProject<Projects.BlazorOrchestrator_Scheduler>("scheduler")
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WithReference(tables).WithReference(queues);

// Agent service
var agent = builder.AddProject<Projects.BlazorOrchestrator_Agent>("agent")
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WithReference(tables).WithReference(queues);

builder.Build().Run();