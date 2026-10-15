using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using BlazorDataOrchestrator.Core.Configuration;
using Microsoft.Extensions.Hosting;

// Auto-detect Docker or Podman before Aspire reads the env var.
ContainerRuntimeDetector.EnsureRuntimeConfigured();

var builder = DistributedApplication.CreateBuilder(args);

// ********************************
// BRING-YOUR-OWN AZURE RESOURCES (publish mode only)
// ********************************
// BlazorData.net (and anyone running `azd up`) can point a deployment at existing Azure
// resources by setting these parameters, for example with `azd env set` or as environment
// variables (Parameters__existingSqlServerName=...). When a parameter is empty the resource is
// created exactly as before; when it is set the existing resource is referenced instead.
// Local `aspire run` ignores all of them.
//
//   existingAcaEnvName / existingAcaEnvResourceGroup             Container Apps environment
//   existingAcrName / existingAcrResourceGroup                   Container registry
//   existingSqlServerName / existingSqlServerResourceGroup       Azure SQL logical server
//   sqlDatabaseName                                              Database name (default blazororchestratordb)
//   existingStorageAccountName / existingStorageResourceGroup    Storage account
//   existingIdentityName / existingIdentityResourceGroup         User-assigned managed identity
//   webAppName / schedulerAppName / agentAppName                 Container app names
//
// A missing *ResourceGroup value means "the resource group being deployed to".
var isPublishMode = builder.ExecutionContext.IsPublishMode;

// Returns the parameter's value when it is set in publish mode, otherwise null.
string? Setting(string key) =>
    isPublishMode && builder.Configuration[$"Parameters:{key}"] is { } value && !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : null;

// Declares the parameter (once), publishing its value as the default so azd does not prompt for it.
var declaredParameters = new Dictionary<string, IResourceBuilder<ParameterResource>>();
IResourceBuilder<ParameterResource>? Parameter(string key)
{
    if (Setting(key) is not { } value) return null;
    if (!declaredParameters.TryGetValue(key, out var parameter))
    {
        parameter = builder.AddParameter(key, value, publishValueAsDefault: true);
        declaredParameters[key] = parameter;
    }
    return parameter;
}

// Marks a resource as existing when its name parameter is set.
void UseExistingWhenSet<T>(IResourceBuilder<T> resource, string nameKey, string resourceGroupKey)
    where T : IAzureResource
{
    if (Parameter(nameKey) is { } name)
    {
        resource.PublishAsExisting(name, Parameter(resourceGroupKey));
    }
}
// ********************************

// Configure the Azure App Container environment
// See https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnet-aspire
// https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnet-aspire
var env = builder.AddAzureContainerAppEnvironment("env");
UseExistingWhenSet(env, "existingAcaEnvName", "existingAcaEnvResourceGroup");

if (Setting("existingAcrName") is not null)
{
    var acr = builder.AddAzureContainerRegistry("acr");
    UseExistingWhenSet(acr, "existingAcrName", "existingAcrResourceGroup");
    env.WithAzureContainerRegistry(acr);
}

// Database configuration:
// - Local dev: SQL Server container via RunAsContainer()
// - Azure deployment (azd up): Managed Azure SQL Database is provisioned automatically,
//   or an existing Azure SQL server is used when existingSqlServerName is set.
//   Users can change the database later via the app's built-in Install Wizard.
var sqlServer = builder.AddAzureSqlServer("sqlserver")
    .RunAsContainer(container =>
    {
        container.WithEnvironment("ACCEPT_EULA", "Y");
        container.WithDataVolume();
        container.WithLifetime(ContainerLifetime.Persistent);
        container.WithEndpoint("tcp", endpoint => endpoint.Port = LocalDevEndpoints.SqlPort);
    });
UseExistingWhenSet(sqlServer, "existingSqlServerName", "existingSqlServerResourceGroup");

var db = sqlServer.AddDatabase("blazororchestratordb", Setting("sqlDatabaseName") ?? "blazororchestratordb");

// Storage configuration:
// - Local dev: Azurite emulator via RunAsEmulator()
// - Azure deployment (azd up): Azure Storage Account is provisioned automatically,
//   or an existing account is used when existingStorageAccountName is set.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithDataVolume();  // Persist Azurite data across restarts
        // Dedicated ports so generated designer projects can reach this Azurite, not a stray one on 10000-10002.
        emulator.WithBlobPort(LocalDevEndpoints.BlobPort)
            .WithQueuePort(LocalDevEndpoints.QueuePort)
            .WithTablePort(LocalDevEndpoints.TablePort);
        // Allow any hostname for table requests — fixes container DNS issues
        // where the agent receives storage.dev.internal instead of 127.0.0.1
        emulator.WithArgs("--disableProductStyleUrl");
    });

UseExistingWhenSet(storage, "existingStorageAccountName", "existingStorageResourceGroup");

var blobs = storage.AddBlobs("blobs");
var tables = storage.AddTables("tables");
var queues = storage.AddQueues("queues");

// Optional shared user-assigned identity. When it is not set, each container app gets its own
// identity, as before.
IResourceBuilder<AzureUserAssignedIdentityResource>? identity = null;
if (Setting("existingIdentityName") is not null)
{
    identity = builder.AddAzureUserAssignedIdentity("bdo-identity");
    UseExistingWhenSet(identity, "existingIdentityName", "existingIdentityResourceGroup");
}

var webAppName = Setting("webAppName");
var schedulerAppName = Setting("schedulerAppName");
var agentAppName = Setting("agentAppName");

// Blazor Server Web App — WithExternalHttpEndpoints for public ingress in ACA
var webApp = builder.AddProject<Projects.BlazorOrchestrator_Web>("webapp")
    .WithExternalHttpEndpoints()
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WithReference(tables).WithReference(queues)
    .PublishAsAzureContainerApp((infrastructure, app) =>
    {
        // Enable ingress session affinity (sticky sessions) so a client's requests
        // are routed to the same replica. Required for Blazor Server's SignalR
        // circuit when scaling beyond a single replica in Azure Container Apps.
        app.Configuration.Ingress.StickySessionsAffinity = StickySessionAffinity.Sticky;
        // The azd-service-name tag lets tools (BlazorData.net update discovery) find each
        // app by its role even when the container app has a custom name.
        app.Tags["azd-service-name"] = "webapp";
        if (webAppName is not null) app.Name = webAppName;
    });

// Scheduler service
var scheduler = builder.AddProject<Projects.BlazorOrchestrator_Scheduler>("scheduler")
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WithReference(tables).WithReference(queues)
    .PublishAsAzureContainerApp((infrastructure, app) =>
    {
        app.Tags["azd-service-name"] = "scheduler";
        if (schedulerAppName is not null) app.Name = schedulerAppName;
    });

// Agent service — uses Dockerfile (AddDockerfile) instead of AddProject so that
// Python 3 is installed in the container image for Python job execution.
// The build context is the src/ directory (one level up from AppHost) so that
// project references to Core and ServiceDefaults resolve correctly.
// There is exactly one agent container app; agentAppName only renames it.
var agent = builder.AddDockerfile("agent", "..", "BlazorOrchestrator.Agent/Dockerfile")
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WaitFor(blobs)
    .WithReference(tables).WaitFor(tables)
    .WithReference(queues).WaitFor(queues)
    .PublishAsAzureContainerApp((infrastructure, app) =>
    {
        app.Tags["azd-service-name"] = "agent";
        if (agentAppName is not null) app.Name = agentAppName;
    });

if (identity is not null)
{
    webApp.WithAzureUserAssignedIdentity(identity);
    scheduler.WithAzureUserAssignedIdentity(identity);
    agent.WithAzureUserAssignedIdentity(identity);
}

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