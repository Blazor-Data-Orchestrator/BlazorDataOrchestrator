namespace BlazorOrchestrator.Web.Data;

public class InstallationModel
{
    // Admin Account
    public string AdminUser { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;

    // Database
    public string DbConnectionType { get; set; } = "managed"; // "managed" or "manual"
    public string DbConnectionString { get; set; } = string.Empty;

    // Storage
    public string BlobType { get; set; } = "managed";
    public string BlobString { get; set; } = string.Empty;
    
    public string TableType { get; set; } = "managed";
    public string TableString { get; set; } = string.Empty;

    public string QueueType { get; set; } = "managed";
    public string QueueString { get; set; } = string.Empty;
}
