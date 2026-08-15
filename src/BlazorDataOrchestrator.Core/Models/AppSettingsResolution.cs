namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// The outcome of resolving a job's effective appsettings from a package.
/// </summary>
public class AppSettingsResolution
{
    /// <summary>The effective settings JSON. Empty when <see cref="IsFatal"/> is true.</summary>
    public string Json { get; set; } = "{}";

    /// <summary>The base file that was used, or null when it was absent.</summary>
    public string? BaseFileUsed { get; set; }

    /// <summary>The environment overlay file that was used, or null when it was absent.</summary>
    public string? OverlayFileUsed { get; set; }

    /// <summary>Non-fatal issues encountered. Never contains configuration values.</summary>
    public List<string> Warnings { get; set; } = new();

    public bool IsFatal { get; set; }

    public string? FatalReason { get; set; }
}
