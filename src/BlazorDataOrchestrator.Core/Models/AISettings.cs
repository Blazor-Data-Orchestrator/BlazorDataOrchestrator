namespace BlazorDataOrchestrator.Core.Models;

using ServiceKind = AIServiceType;

/// <summary>
/// Settings for a single AI service provider. Each provider keeps its own
/// API key and provider specific configuration.
/// </summary>
public class AIProviderSettings
{
    public ServiceKind ServiceType { get; set; }
    public string ApiKey { get; set; } = "";
    public string AIModel { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string ApiVersion { get; set; } = "";

    /// <summary>
    /// Optional explicit Azure OpenAI deployment path.
    /// Example: "openai/deployments/my-gpt4o" or a full custom route.
    /// When empty, the SDK default convention is used.
    /// </summary>
    public string DeploymentPath { get; set; } = "";

    public string EmbeddingModel { get; set; } = "";

    /// <summary>
    /// Azure AI Foundry wire protocol, stored as a <see cref="FoundryApiProtocol"/> name.
    /// Empty means <see cref="FoundryApiProtocol.Auto"/>.
    /// </summary>
    public string ApiProtocol { get; set; } = "";

    public FoundryApiProtocol ParsedApiProtocol =>
        Enum.TryParse<FoundryApiProtocol>(ApiProtocol, ignoreCase: true, out var protocol)
            ? protocol
            : FoundryApiProtocol.Auto;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public AIProviderSettings Clone() => new()
    {
        ServiceType = ServiceType,
        ApiKey = ApiKey,
        AIModel = AIModel,
        Endpoint = Endpoint,
        ApiVersion = ApiVersion,
        DeploymentPath = DeploymentPath,
        EmbeddingModel = EmbeddingModel,
        ApiProtocol = ApiProtocol
    };
}

/// <summary>
/// Container holding the settings for every AI provider plus the active selection.
/// </summary>
public class AISettings
{
    public ServiceKind ActiveServiceType { get; set; } = ServiceKind.OpenAI;

    public Dictionary<ServiceKind, AIProviderSettings> Providers { get; set; } = new();

    public AIProviderSettings Active => GetOrCreate(ActiveServiceType);

    public AIProviderSettings GetOrCreate(ServiceKind type)
    {
        if (!Providers.TryGetValue(type, out var provider))
        {
            provider = new AIProviderSettings { ServiceType = type };
            Providers[type] = provider;
        }

        return provider;
    }

    public bool IsConfigured => Active.IsConfigured;

    public AISettings Clone()
    {
        var clone = new AISettings { ActiveServiceType = ActiveServiceType };

        foreach (var pair in Providers)
        {
            clone.Providers[pair.Key] = pair.Value.Clone();
        }

        return clone;
    }

    #region Compatibility shim

    // Pass-through members that keep existing call sites compiling while they are
    // migrated to the per-provider model. Remove once no references remain.

    [Obsolete("Use ActiveServiceType.")]
    public string AIServiceType
    {
        get => AIServiceTypes.ToDisplayName(ActiveServiceType);
        set => ActiveServiceType = AIServiceTypes.ParseOrDefault(value);
    }

    [Obsolete("Use Active.ApiKey.")]
    public string ApiKey
    {
        get => Active.ApiKey;
        set => Active.ApiKey = value ?? "";
    }

    [Obsolete("Use Active.AIModel.")]
    public string AIModel
    {
        get => Active.AIModel;
        set => Active.AIModel = value ?? "";
    }

    [Obsolete("Use Active.Endpoint.")]
    public string Endpoint
    {
        get => Active.Endpoint;
        set => Active.Endpoint = value ?? "";
    }

    [Obsolete("Use Active.ApiVersion.")]
    public string ApiVersion
    {
        get => Active.ApiVersion;
        set => Active.ApiVersion = value ?? "";
    }

    [Obsolete("Use Active.DeploymentPath.")]
    public string DeploymentPath
    {
        get => Active.DeploymentPath;
        set => Active.DeploymentPath = value ?? "";
    }

    [Obsolete("Use Active.EmbeddingModel.")]
    public string EmbeddingModel
    {
        get => Active.EmbeddingModel;
        set => Active.EmbeddingModel = value ?? "";
    }

    #endregion
}
