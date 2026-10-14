using System.Text.RegularExpressions;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Detects connection strings that the hosting environment already provides as environment
/// variables (for example ConnectionStrings__blazororchestratordb set by azd / Aspire in Azure
/// Container Apps). The Install Wizard shows these instead of asking for them, and never writes
/// them to appsettings.json: environment variables override appsettings.json anyway, and the
/// container file system in Azure Container Apps is not durable.
/// </summary>
public class EnvironmentConnectionStrings
{
    public const string ProvidedByAzureText = "Provided by Azure deployment";
    public const string ProvidedByEnvironmentText = "Provided by environment variable";

    private readonly Func<string, string?> _getEnvironmentVariable;

    public EnvironmentConnectionStrings()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    public EnvironmentConnectionStrings(Func<string, string?> getEnvironmentVariable)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
    }

    /// <summary>
    /// True when running in Azure Container Apps (the platform sets CONTAINER_APP_NAME).
    /// </summary>
    public bool IsRunningInContainerApps => !string.IsNullOrWhiteSpace(_getEnvironmentVariable("CONTAINER_APP_NAME"));

    /// <summary>
    /// Returns the connection string set by the environment (ConnectionStrings__{name}), or null.
    /// </summary>
    public string? GetProvided(string name)
    {
        var value = _getEnvironmentVariable($"ConnectionStrings__{name}");
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public bool IsProvided(string name) => GetProvided(name) != null;

    /// <summary>
    /// appsettings.json may only be written when the value is not provided by the environment and
    /// the app is not running in Azure Container Apps.
    /// </summary>
    public bool CanWriteAppSettings(params string[] names) =>
        !IsRunningInContainerApps && names.All(name => !IsProvided(name));

    public string ProvidedByText => IsRunningInContainerApps ? ProvidedByAzureText : ProvidedByEnvironmentText;

    /// <summary>
    /// Hides secrets (passwords and storage keys) in a connection string for display.
    /// </summary>
    public static string Mask(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return string.Empty;

        return Regex.Replace(
            connectionString,
            @"(?<key>\b(Password|Pwd|AccountKey|SharedAccessSignature|SharedAccessKey)\s*=)[^;]*",
            "${key}********",
            RegexOptions.IgnoreCase);
    }
}
