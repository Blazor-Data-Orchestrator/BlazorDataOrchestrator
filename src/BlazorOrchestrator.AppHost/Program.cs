using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

// Auto-detect Docker or Podman before Aspire reads the env var.
ContainerRuntimeDetector.EnsureRuntimeConfigured();

var builder = DistributedApplication.CreateBuilder(args);

// Configure the Azure App Container environment
// See https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnet-aspire
// https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnet-aspire
builder.AddAzureContainerAppEnvironment("env");

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
        container.WithEndpoint("tcp", endpoint => endpoint.Port = 14330);
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

// Agent service — uses Dockerfile (AddDockerfile) instead of AddProject so that
// Python 3 is installed in the container image for Python job execution.
// The build context is the src/ directory (one level up from AppHost) so that
// project references to Core and ServiceDefaults resolve correctly.
var agent = builder.AddDockerfile("agent", "..", "BlazorOrchestrator.Agent/Dockerfile")
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WithReference(tables).WithReference(queues);

builder.Build().Run();

// ---------------------------------------------------------------------------
// Container runtime auto-detection (Docker / Podman)
// ---------------------------------------------------------------------------
static class ContainerRuntimeDetector
{
    private const string EnvVar = "DOTNET_ASPIRE_CONTAINER_RUNTIME";

    public static void EnsureRuntimeConfigured()
    {
        var existing = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrEmpty(existing))
        {
            Console.WriteLine($"[AppHost] Container runtime (explicit): {existing}");
            return;
        }

        if (IsRuntimeAvailable("docker"))
        {
            Environment.SetEnvironmentVariable(EnvVar, "docker");
            Console.WriteLine("[AppHost] Auto-detected container runtime: docker");
        }
        else if (IsRuntimeAvailable("podman"))
        {
            Environment.SetEnvironmentVariable(EnvVar, "podman");
            Console.WriteLine("[AppHost] Auto-detected container runtime: podman");
        }
        else
        {
            Console.WriteLine(
                "[AppHost] WARNING: No container runtime found (docker, podman). "
                + "Container-based resources will fail to start.");
        }
    }

    private static bool IsRuntimeAvailable(string command)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}