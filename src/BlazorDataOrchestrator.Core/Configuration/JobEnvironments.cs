namespace BlazorDataOrchestrator.Core.Configuration;

/// <summary>
/// Canonical job environment names and the appsettings file naming convention.
/// </summary>
public static class JobEnvironments
{
    public const string Development = "Development";
    public const string Staging = "Staging";
    public const string Production = "Production";

    /// <summary>New jobs and unrecognised values resolve here.</summary>
    public const string Default = Production;

    public const string BaseFileName = "appsettings.json";

    public static readonly IReadOnlyList<string> All =
        new[] { Development, Staging, Production };

    public static readonly IReadOnlyList<string> AllFileNames =
        new[] { BaseFileName }
            .Concat(All.Select(e => $"appsettings.{e}.json"))
            .ToArray();

    public static string GetFileName(string? environment) =>
        $"appsettings.{Normalize(environment)}.json";

    public static string Normalize(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "production" or "prod" => Production,
        "staging" or "stage" or "uat" => Staging,
        "development" or "dev" or "local" or "designer" => Development,
        _ => Default
    };

    /// <summary>True when the raw value maps to a canonical environment without falling back to the default.</summary>
    public static bool IsRecognized(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant() is
        "production" or "prod" or "staging" or "stage" or "uat"
        or "development" or "dev" or "local" or "designer";
}
