namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Connection strings set by the hosting environment (azd / Aspire set
/// <c>ConnectionStrings__blazororchestratordb</c>, <c>__blobs</c>, <c>__tables</c>, <c>__queues</c>
/// on each Azure Container App). The Install Wizard shows these as provided and never writes
/// them, or anything else, to appsettings.json when they are set or when it runs in Azure
/// Container Apps, whose file system is not durable. The values themselves stay on the server:
/// they are never copied into the wizard model or rendered.
/// </summary>
public sealed class EnvironmentConnectionStrings
{
    public const string ProvidedByText = "Provided by the Azure deployment";
    public const string ContainerAppNameVariable = "CONTAINER_APP_NAME";

    private readonly Func<string, string?> _getEnvironmentVariable;

    public EnvironmentConnectionStrings() : this(Environment.GetEnvironmentVariable)
    {
    }

    public EnvironmentConnectionStrings(Func<string, string?> getEnvironmentVariable)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
    }

    /// <summary>True in Azure Container Apps (the platform sets CONTAINER_APP_NAME).</summary>
    public bool IsRunningInContainerApps => !string.IsNullOrWhiteSpace(_getEnvironmentVariable(ContainerAppNameVariable));

    /// <summary>True when ConnectionStrings__<paramref name="name"/> is set by the environment.</summary>
    public bool IsProvided(string name) =>
        !string.IsNullOrWhiteSpace(_getEnvironmentVariable($"ConnectionStrings__{name}"))
        || !string.IsNullOrWhiteSpace(_getEnvironmentVariable($"ConnectionStrings:{name}"));

    /// <summary>
    /// The wizard may write these connection strings to appsettings.json only outside Azure
    /// Container Apps and only when the environment does not provide any of them (an environment
    /// variable would override the file anyway).
    /// </summary>
    public bool CanWriteAppSettings(params string[] names) =>
        !IsRunningInContainerApps && names.All(name => !IsProvided(name));

    /// <summary>Why appsettings.json is not written, for the wizard UI.</summary>
    public string NotWrittenReason(params string[] names) =>
        IsRunningInContainerApps
            ? "Running in Azure Container Apps: appsettings.json was not changed because the container file system is not durable. " +
              $"Set {string.Join(", ", names.Select(n => $"ConnectionStrings__{n}"))} on the container app instead."
            : $"appsettings.json was not changed because the environment already provides {string.Join(", ", names.Where(IsProvided).Select(n => $"ConnectionStrings__{n}"))}.";
}
