namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Configuration for a job, typically read from configuration.json in the NuGet package.
/// </summary>
public class JobConfiguration
{
    /// <summary>
    /// The selected programming language for the job (CSharp or Python).
    /// </summary>
    public string SelectedLanguage { get; set; } = "CSharp";

    /// <summary>
    /// Additional settings for the job execution.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = new();

    /// <summary>
    /// The entry point file name (e.g., main.cs or main.py).
    /// </summary>
    public string? EntryPoint { get; set; }

    /// <summary>
    /// List of required NuGet packages for C# execution.
    /// </summary>
    public List<string> NuGetPackages { get; set; } = new();

    /// <summary>
    /// List of required Python packages (pip requirements).
    /// </summary>
    public List<string> PythonPackages { get; set; } = new();
}
