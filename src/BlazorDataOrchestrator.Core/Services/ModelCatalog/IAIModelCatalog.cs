using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Outcome of a provider model listing call.
/// </summary>
public enum ModelListStatus
{
    Success,
    InvalidKey,
    Unreachable,
    Empty,
    NotConfigured
}

/// <summary>
/// Result of listing models for a provider. There is deliberately no hard-coded
/// fallback list; failures are surfaced to the caller.
/// </summary>
public sealed record ModelListResult(
    IReadOnlyList<string> Models,
    ModelListStatus Status,
    string? ErrorMessage)
{
    public static ModelListResult Success(IReadOnlyList<string> models) =>
        models.Count == 0
            ? Empty()
            : new ModelListResult(models, ModelListStatus.Success, null);

    public static ModelListResult Empty() =>
        new(Array.Empty<string>(), ModelListStatus.Empty, "No models available for this key.");

    public static ModelListResult InvalidKey() =>
        new(Array.Empty<string>(), ModelListStatus.InvalidKey, "Invalid API key.");

    public static ModelListResult Unreachable(string? detail = null) =>
        new(Array.Empty<string>(), ModelListStatus.Unreachable,
            detail is null ? "Could not reach provider. Use Refresh to try again." : $"Could not reach provider: {detail}");

    public static ModelListResult NotConfigured(string message) =>
        new(Array.Empty<string>(), ModelListStatus.NotConfigured, message);
}

/// <summary>
/// Lists the models available for a provider using that provider's live API.
/// </summary>
public interface IAIModelCatalog
{
    ServiceKind ServiceType { get; }

    Task<ModelListResult> ListModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken);
}
