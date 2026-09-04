namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Indexes the registered model catalogs by service type.
/// </summary>
public class AIProviderRegistry
{
    private readonly Dictionary<ServiceKind, IAIModelCatalog> _catalogs;

    public AIProviderRegistry(IEnumerable<IAIModelCatalog> catalogs)
    {
        _catalogs = catalogs.ToDictionary(c => c.ServiceType);
    }

    public IAIModelCatalog? GetCatalog(ServiceKind serviceType)
        => _catalogs.TryGetValue(serviceType, out var catalog) ? catalog : null;
}
