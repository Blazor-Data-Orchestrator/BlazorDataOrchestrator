namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Model class for AI settings.
/// </summary>
public class AISettings
{
    public string AIServiceType { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = "";
    public string AIModel { get; set; } = "gpt-4-turbo-preview";
    public string Endpoint { get; set; } = "";
    public string ApiVersion { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";

    /// <summary>
    /// Optional explicit Azure OpenAI deployment path.
    /// Example: "openai/deployments/my-gpt4o" or a full custom route.
    /// When empty, the SDK default convention is used.
    /// </summary>
    public string DeploymentPath { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
