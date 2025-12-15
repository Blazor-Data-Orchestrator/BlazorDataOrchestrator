using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Define a secret parameter for SA
var saPassword = builder.AddParameter("sqlServer-password", secret: true);

// Add a SQL Server container (persistent)
var sqlServer = builder.AddSqlServer("sqlServer", saPassword)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// Create database without running schema script here (schema init moved to Web project)
var db = sqlServer.AddDatabase("blazororchestratordb");

// Add Azure Storage using the correct method for Aspire
var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");

// Blazor Server Web App
var webApp = builder.AddProject<Projects.BlazorOrchistrator_Web>("webapp")
    .WithReference(db)
    .WaitFor(db);

// Scheduler service
var scheduler = builder.AddProject<Projects.BlazorOrchistrator_Scheduler>("scheduler")
    .WithReference(db)
    .WaitFor(db);

// Agent service - changed from container to project
var agent = builder.AddProject<Projects.BlazorOrchistrator_Agent>("agent")
    .WithReference(db)
    .WithReference(queues)
    .WithReference(blobs)
    .WaitFor(db);

builder.Build().Run();