namespace BlazorOrchestrator.Web.Data;

public class InstallationModel
{
    // Admin Account - pre-filled with defaults
    public string AdminUser { get; set; } = "admin";
    public string AdminEmail { get; set; } = "admin@localhost";
    public string AdminPassword { get; set; } = "Admin@123";

    // Database
    public string DbConnectionType { get; set; } = "manual"; // "managed" or "manual"
    public string DbConnectionString { get; set; } = string.Empty;
    public bool DbConnectionVerified { get; set; } = false;
    public bool DbCreated { get; set; } = false;

    // Set when the environment (azd / Aspire) provides the connection string. The value itself
    // stays on the server and is never copied into this model.
    public bool DbProvidedByEnvironment { get; set; }

    // Storage
    public string BlobType { get; set; } = "manual";
    public string BlobString { get; set; } = string.Empty;
    public bool BlobVerified { get; set; } = false;
    public bool BlobProvidedByEnvironment { get; set; }

    public string TableType { get; set; } = "manual";
    public string TableString { get; set; } = string.Empty;
    public bool TableVerified { get; set; } = false;
    public bool TableProvidedByEnvironment { get; set; }

    public string QueueType { get; set; } = "manual";
    public string QueueString { get; set; } = string.Empty;
    public bool QueueVerified { get; set; } = false;
    public bool QueueProvidedByEnvironment { get; set; }
}
