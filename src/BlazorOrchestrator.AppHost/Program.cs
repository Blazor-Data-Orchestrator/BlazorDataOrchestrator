using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// ********************************
// BRING-YOUR-OWN AZURE RESOURCES (publish mode only)
// ********************************
// BlazorData.net (and anyone running `azd up`) can point the deployment at existing Azure
// resources by setting these parameters, for example as environment variables
// (Parameters__existingSqlServerName=...) before running azd. When a parameter is empty the
// resource is created exactly as before; when it is set the existing resource is referenced.
// Local `aspire run` ignores all of them.
//
//   existingAcaEnvName / existingAcaEnvResourceGroup             Container Apps environment
//   existingAcrName / existingAcrResourceGroup                   Container registry
//   existingSqlServerName / existingSqlServerResourceGroup       Azure SQL logical server
//   sqlDatabaseName                                              Database name (default blazororchestratordb)
//   existingStorageAccountName / existingStorageResourceGroup    Storage account
//   existingIdentityName / existingIdentityResourceGroup         User-assigned managed identity
//   webAppName / schedulerAppName / agentAppName                 Container app names
var isPublishMode = builder.ExecutionContext.IsPublishMode;

// Returns the parameter's value when it is set in publish mode, otherwise null.
string? Setting(string key) =>
    isPublishMode && builder.Configuration[$"Parameters:{key}"] is { Length: > 0 } value ? value.Trim() : null;

// Declares the parameter (once) with its value published as the default, so azd does not prompt for it.
var parameters = new Dictionary<string, IResourceBuilder<ParameterResource>>();
IResourceBuilder<ParameterResource>? Parameter(string key)
{
    if (Setting(key) is not { } value) return null;
    if (!parameters.TryGetValue(key, out var parameter))
    {
        parameter = builder.AddParameter(key, value, publishValueAsDefault: true);
        parameters[key] = parameter;
    }
    return parameter;
}

// Marks a resource as existing when its name parameter is set. A missing resource group means
// "the resource group being deployed to".
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

if (Setting("existingAcaEnvName") is not null)
{
    // Aspire 13.2.0 records PublishAsExisting on the environment but still emits a new managed
    // environment (newer Aspire versions honor it). Swap the generated environment, its Log
    // Analytics workspace and its dashboard for a reference to the existing environment,
    // keeping the outputs the container app modules consume.
    env.ConfigureInfrastructure(infra =>
    {
        string[] replacedOutputs =
        [
            "AZURE_LOG_ANALYTICS_WORKSPACE_NAME",
            "AZURE_LOG_ANALYTICS_WORKSPACE_ID",
            "AZURE_CONTAINER_APPS_ENVIRONMENT_NAME",
            "AZURE_CONTAINER_APPS_ENVIRONMENT_ID",
            "AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN"
        ];

        foreach (var provisionable in infra.GetProvisionableResources().ToList())
        {
            if (provisionable is ContainerAppManagedEnvironment
                || provisionable is Azure.Provisioning.OperationalInsights.OperationalInsightsWorkspace
                || (provisionable is Azure.Provisioning.Primitives.ProvisionableResource resource
                    && resource.ResourceType.ToString().StartsWith("Microsoft.App/managedEnvironments/", StringComparison.OrdinalIgnoreCase))
                || (provisionable is Azure.Provisioning.ProvisioningOutput output && replacedOutputs.Contains(output.BicepIdentifier)))
            {
                infra.Remove(provisionable);
            }
        }

        var existingEnv = ContainerAppManagedEnvironment.FromExisting("env");
        AzureProvisioningResource.TryApplyExistingResourceAnnotation(env.Resource, infra, existingEnv);
        infra.Add(existingEnv);

        infra.Add(new Azure.Provisioning.ProvisioningOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_NAME", typeof(string)) { Value = existingEnv.Name });
        infra.Add(new Azure.Provisioning.ProvisioningOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_ID", typeof(string)) { Value = existingEnv.Id });
        infra.Add(new Azure.Provisioning.ProvisioningOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN", typeof(string)) { Value = existingEnv.DefaultDomain });
    });
}

if (Setting("existingAcrName") is not null)
{
    var acr = builder.AddAzureContainerRegistry("acr");
    UseExistingWhenSet(acr, "existingAcrName", "existingAcrResourceGroup");
    env.WithAzureContainerRegistry(acr);

    if (Setting("existingAcrResourceGroup") is not null)
    {
        // Aspire 13.2.0 puts the environment identity's AcrPull role assignment in the environment
        // module, which cannot target a registry in another resource group (Bicep BCP139). Move
        // that assignment into its own module deployed to the registry's resource group.
        const string PrincipalIdOutput = "AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_PRINCIPAL_ID";
        env.ConfigureInfrastructure(infra =>
        {
            foreach (var roleAssignment in infra.GetProvisionableResources().OfType<Azure.Provisioning.Authorization.RoleAssignment>().ToList())
            {
                infra.Remove(roleAssignment);
            }

            var registryIdentity = infra.GetProvisionableResources().OfType<Azure.Provisioning.Roles.UserAssignedIdentity>().Single();
            infra.Add(new Azure.Provisioning.ProvisioningOutput(PrincipalIdOutput, typeof(string)) { Value = registryIdentity.PrincipalId });
        });

        var acrNameParameter = Parameter("existingAcrName")!;
        var acrPull = builder.AddAzureInfrastructure("acr-pull", infra =>
            {
                var registry = Azure.Provisioning.ContainerRegistry.ContainerRegistryService.FromExisting("acr");
                registry.Name = acrNameParameter.AsProvisioningParameter(infra);
                infra.Add(registry);

                var principalId = new BicepOutputReference(PrincipalIdOutput, env.Resource).AsProvisioningParameter(infra, "principalId");
                infra.Add(registry.CreateRoleAssignment(
                    Azure.Provisioning.ContainerRegistry.ContainerRegistryBuiltInRole.AcrPull,
                    Azure.Provisioning.Authorization.RoleManagementPrincipalType.ServicePrincipal,
                    principalId));
            });

        // Deploy this module to the registry's resource group.
        acrPull.Resource.Scope = new AzureBicepResourceScope(Parameter("existingAcrResourceGroup")!.Resource);
    }
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
        container.WithEndpoint("tcp", endpoint => endpoint.Port = 14330);
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
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithDataVolume();  // Persist Azurite data across restarts
        emulator.WithEndpoint("blob", endpoint => endpoint.Port = 10000);
        emulator.WithEndpoint("queue", endpoint => endpoint.Port = 10001);
        emulator.WithEndpoint("table", endpoint => endpoint.Port = 10002);
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
// There is exactly one agent container app; agentAppName only renames it.
var agent = builder.AddDockerfile("agent", "..", "BlazorOrchestrator.Agent/Dockerfile")
    .WithReference(db).WaitFor(db)
    .WithReference(blobs).WithReference(tables).WithReference(queues);

if (isPublishMode)
{
    var webAppName = Setting("webAppName");
    var schedulerAppName = Setting("schedulerAppName");
    var agentAppName = Setting("agentAppName");

    // The azd-service-name tag lets tools (BlazorData.net update discovery) find each app by its
    // role even when the container app has a custom name.
    webApp.PublishAsAzureContainerApp((infra, app) =>
    {
        // Blazor Server keeps each circuit on one replica, so a user must stay on that replica.
        app.Configuration.Ingress.StickySessionsAffinity = StickySessionAffinity.Sticky;
        app.Tags.Add("azd-service-name", "webapp");
        if (webAppName is not null) app.Name = webAppName;
    });

    scheduler.PublishAsAzureContainerApp((infra, app) =>
    {
        app.Tags.Add("azd-service-name", "scheduler");
        if (schedulerAppName is not null) app.Name = schedulerAppName;
    });

    agent.PublishAsAzureContainerApp((infra, app) =>
    {
        app.Tags.Add("azd-service-name", "agent");
        if (agentAppName is not null) app.Name = agentAppName;
    });

    if (identity is not null)
    {
        webApp.WithAzureUserAssignedIdentity(identity);
        scheduler.WithAzureUserAssignedIdentity(identity);
        agent.WithAzureUserAssignedIdentity(identity);
    }
}

builder.Build().Run();
