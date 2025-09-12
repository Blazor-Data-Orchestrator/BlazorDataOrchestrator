using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Get the contents of: !SQL\01.00.00.sql
// This script is used to create the database and tables for the LocalInternalAIChatBot application.
// It is assumed that the script is located in the same directory as this Program.cs file.
// If the script is located elsewhere, adjust the path accordingly.
string scriptPath = Path.Combine(AppContext.BaseDirectory, "!SQL", "01.00.00.sql");
string sqlScript = await File.ReadAllTextAsync(scriptPath);

// Define a secret parameter for SA
var saPassword = builder.AddParameter("sqlServer-password", secret: true);

// Pass that parameter into AddSqlServer
// Add a SQL Server container
var sqlServer = builder.AddSqlServer("sqlServer", saPassword)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var db = sqlServer.AddDatabase("blazororchestratordb")
    .WithCreationScript(sqlScript);

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