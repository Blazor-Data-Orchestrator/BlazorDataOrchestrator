namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Context information passed to job execution methods.
/// </summary>
public class JobExecutionContext
{
    /// <summary>
    /// The ID of the job being executed.
    /// </summary>
    public int JobId { get; set; }

    /// <summary>
    /// The ID of the job instance being executed.
    /// </summary>
    public int JobInstanceId { get; set; }

    /// <summary>
    /// The ID of the job schedule that triggered this execution.
    /// </summary>
    public int JobScheduleId { get; set; }

    /// <summary>
    /// The selected programming language for execution (CSharp or Python).
    /// </summary>
    public string SelectedLanguage { get; set; } = "CSharp";

    /// <summary>
    /// The app settings JSON string containing connection strings and configuration.
    /// </summary>
    public string AppSettingsJson { get; set; } = "{}";

    /// <summary>
    /// The SQL connection string for database operations.
    /// </summary>
    public string SqlConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The Azure Table Storage connection string for logging.
    /// </summary>
    public string TableConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The Agent ID processing this job (optional).
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// The WebAPI parameter passed to the job (optional).
    /// </summary>
    public string WebAPIParameter { get; set; } = string.Empty;

    /// <summary>
    /// The environment the job is running under (e.g., Production, Staging, Development).
    /// Used to determine which appsettings file was loaded from the NuGet package.
    /// </summary>
    public string Environment { get; set; } = "Development";
}

