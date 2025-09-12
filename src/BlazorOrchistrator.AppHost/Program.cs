var builder = DistributedApplication.CreateBuilder(args);

// Storage infrastructure
var storage = builder.AddAzureStorage("storage");
var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");
var sql = builder.AddSqlServer("sql").AddDatabase("database");

// Blazor Server Web App
var webApp = builder.AddProject("webapp", "../BlazorOrchistrator.Web/BlazorOrchistrator.Web.csproj")
    .WithReference(sql);

// Scheduler service
var scheduler = builder.AddProject("scheduler", "../BlazorOrchistrator.Scheduler/BlazorOrchistrator.Scheduler.csproj")
    .WithReference(queues)
    .WithReference(blobs)
    .WithReference(sql);

// Agent container with 2 replicas
var agent = builder.AddContainer("agent", "ghcr.io/yourorg/BlazorOrchistrator-agent:latest")
    .WithReference(queues)
    .WithReference(blobs)
    .WithReference(sql)
    .WithEnvironment("REPLICAS", "2");

var app = builder.Build();
app.Run();
