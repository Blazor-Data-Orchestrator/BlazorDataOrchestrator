using System.Net;
using BlazorDataOrchestrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services.ModelCatalog;

using ServiceKind = BlazorDataOrchestrator.Core.Models.AIServiceType;

/// <summary>
/// Shared HTTP plumbing for the provider catalogs: one retry on a transient failure,
/// a 10 second timeout, and status mapping that never logs the API key.
/// </summary>
public abstract class HttpModelCatalogBase : IAIModelCatalog
{
    /// <summary>Named <see cref="HttpClient"/> used by every catalog.</summary>
    public const string HttpClientName = "AIModelCatalog";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    protected HttpModelCatalogBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public abstract ServiceKind ServiceType { get; }

    public abstract Task<ModelListResult> ListModelsAsync(AIProviderSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the request, retrying once on a transient 5xx or timeout.
    /// Returns null when the response was successful and <paramref name="body"/> is populated.
    /// </summary>
    protected async Task<ModelListResult?> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        Action<string> onSuccessBody)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = RequestTimeout;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = requestFactory();
                using var response = await client.SendAsync(request, cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("{Provider} model listing rejected the API key ({StatusCode}).",
                        ServiceType, (int)response.StatusCode);
                    return ModelListResult.InvalidKey();
                }

                if ((int)response.StatusCode >= 500 && attempt == 0)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("{Provider} model listing failed with {StatusCode}.",
                        ServiceType, (int)response.StatusCode);
                    return ModelListResult.Unreachable($"HTTP {(int)response.StatusCode}");
                }

                onSuccessBody(await response.Content.ReadAsStringAsync(cancellationToken));
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (attempt == 1)
                {
                    _logger.LogWarning(ex, "{Provider} model listing could not reach the provider.", ServiceType);
                    return ModelListResult.Unreachable(ex.Message);
                }
            }
        }

        return ModelListResult.Unreachable();
    }
}
