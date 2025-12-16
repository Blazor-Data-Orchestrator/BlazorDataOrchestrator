using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

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

// Azurite (Storage Emulator)
var azurite = builder.AddContainer("azurite", "mcr.microsoft.com/azure-storage/azurite")
    .WithArgs("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0", "--loose")
    .WithEndpoint(name: "blob", scheme: "http", port: 10000, targetPort: 10000)
    .WithEndpoint(name: "queue", scheme: "http", port: 10001, targetPort: 10001)
    .WithEndpoint(name: "table", scheme: "http", port: 10002, targetPort: 10002)
    .WithLifetime(ContainerLifetime.Persistent);

// Build an Azurite connection string to hand to services
// (devstoreaccount1 is Azurite's fixed dev account)
string StorageConnString(Func<string, string> ep) =>
    $"DefaultEndpointsProtocol=http;" +
    $"AccountName=devstoreaccount1;" +
    $"AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
    $"BlobEndpoint={ep("blob")};" +
    $"QueueEndpoint={ep("queue")};" +
    $"TableEndpoint={ep("table")}";

// Helper to read the runtime-resolved endpoints
string ResolveStorageConnString() => StorageConnString(name => 
{
    var port = name switch 
    {
        "blob" => 10000,
        "queue" => 10001,
        "table" => 10002,
        _ => throw new InvalidOperationException($"Unknown endpoint: {name}")
    };
    return $"http://127.0.0.1:{port}/devstoreaccount1";
});

// Blazor Server Web App
var webApp = builder.AddProject<Projects.BlazorOrchistrator_Web>("webapp")
    .WithReference(db)
    // Supply storage connection via environment var your service reads
    .WithEnvironment("AZURE_STORAGE_CONNECTION_STRING", ResolveStorageConnString())
    .WaitFor(db);

// Scheduler service
var scheduler = builder.AddProject<Projects.BlazorOrchistrator_Scheduler>("scheduler")
    .WithReference(db)
    // Supply storage connection via environment var your service reads
    .WithEnvironment("AZURE_STORAGE_CONNECTION_STRING", ResolveStorageConnString())
    .WaitFor(db);

// Agent service - changed from container to project
var agent = builder.AddProject<Projects.BlazorOrchistrator_Agent>("agent")
    .WithReference(db)
    // Supply storage connection via environment var your service reads
    .WithEnvironment("AZURE_STORAGE_CONNECTION_STRING", ResolveStorageConnString())
    .WaitFor(db);

builder.Build().Run();