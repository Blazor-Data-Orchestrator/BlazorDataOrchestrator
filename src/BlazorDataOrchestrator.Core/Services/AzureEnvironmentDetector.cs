namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Determines whether the application is running inside
/// Azure Container Apps or locally.
/// </summary>
public static class AzureEnvironmentDetector
{
    /// <summary>
    /// Azure Container Apps injects CONTAINER_APP_NAME automatically
    /// into every revision. This is the most reliable signal.
    /// </summary>
    public static bool IsAzureContainerApp =>
        !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"));

    /// <summary>
    /// Fallback: checks whether the given base URI is non-loopback.
    /// Intended for Razor components that have access to NavigationManager.
    /// </summary>
    public static bool IsRemoteHost(string baseUri)
    {
        try { return !new Uri(baseUri).IsLoopback; }
        catch { return false; }
    }
}
