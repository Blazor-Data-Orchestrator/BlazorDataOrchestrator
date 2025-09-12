using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// SQL Server database setup with local container
var sqlPassword = builder.AddParameter("sqlPassword", secret: true);
var sql = builder.AddSqlServer("sql", password: sqlPassword, port: 1433)
    .WithDataVolume()
    .AddDatabase("database");

// Storage infrastructure
var storage = builder.AddAzureStorage("storage");
var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");

// Blazor Server Web App
var webApp = builder.AddProject<Projects.BlazorOrchistrator_Web>("webapp")
    .WithReference(sql)
    .WaitFor(sql);

// Scheduler service
var scheduler = builder.AddProject<Projects.BlazorOrchistrator_Scheduler>("scheduler")
    .WithReference(queues)
    .WithReference(blobs)
    .WithReference(sql)
    .WaitFor(sql);

// Agent container with 2 replicas
var agent = builder.AddContainer("agent", "ghcr.io/yourorg/BlazorOrchistrator-agent:latest")
    .WithReference(queues)
    .WithReference(blobs)
    .WithReference(sql)
    .WithEnvironment("REPLICAS", "2")
    .WaitFor(sql);

builder.Build().Run();