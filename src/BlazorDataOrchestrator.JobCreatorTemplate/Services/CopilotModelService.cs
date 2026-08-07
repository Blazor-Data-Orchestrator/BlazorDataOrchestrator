using GitHub.Copilot;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services;

/// <summary>
/// Discovers and caches the list of Copilot models available to the current user.
/// Fetches models exclusively from the GitHub Copilot SDK API.
/// </summary>
public class CopilotModelService
{
    private readonly CopilotClient _client;
    private readonly ILogger<CopilotModelService> _logger;

    private List<string>? _cachedModels;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public CopilotModelService(CopilotClient client, ILogger<CopilotModelService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Timestamp of the last successful model refresh, if any.
    /// </summary>
    public DateTime? LastRefreshed { get; private set; }

    /// <summary>
    /// Checks if the Copilot API is currently available.
    /// </summary>
    public async Task<bool> IsApiAvailableAsync()
    {
        try
        {
            await _client.GetStatusAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the list of available models from the Copilot API.
    /// Throws an exception if the API is unavailable or returns no models.
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync(bool forceRefresh = false)
    {
        // Return cached if valid and not forcing refresh
        if (!forceRefresh && _cachedModels != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedModels;
        }

        // Verify connectivity
        if (!await IsApiAvailableAsync())
        {
            _logger.LogWarning("Copilot API not available");
            throw new InvalidOperationException(
                "Cannot fetch models: Copilot CLI is not connected. " +
                "Please ensure the CLI is installed and authenticated.");
        }

        // Fetch from API
        var models = await FetchModelsFromSdkAsync();

        if (models == null || models.Count == 0)
        {
            throw new InvalidOperationException(
                "No models returned from the Copilot API. " +
                "This may indicate a subscription or permissions issue.");
        }

        _cachedModels = models.OrderBy(m => m).ToList();
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
        LastRefreshed = DateTime.UtcNow;

        _logger.LogInformation("Fetched {Count} models from Copilot API", models.Count);
        return _cachedModels;
    }

    /// <summary>
    /// Attempts to list models via the SDK. Returns null if the method is not available.
    /// </summary>
    private async Task<List<string>?> FetchModelsFromSdkAsync()
    {
        try
        {
            // The Copilot SDK may expose ListModelsAsync on the client.
            // Use reflection to check at runtime so we don't break if the method doesn't exist.
            var method = _client.GetType().GetMethod("ListModelsAsync");
            if (method != null)
            {
                // Build default arguments for every parameter the method expects
                // (e.g. CancellationToken, options objects added in newer SDK versions).
                var methodParams = method.GetParameters();
                var args = new object?[methodParams.Length];
                for (int i = 0; i < methodParams.Length; i++)
                {
                    if (methodParams[i].HasDefaultValue)
                    {
                        args[i] = methodParams[i].DefaultValue;
                    }
                    else if (methodParams[i].ParameterType == typeof(CancellationToken))
                    {
                        args[i] = CancellationToken.None;
                    }
                    else if (methodParams[i].ParameterType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(methodParams[i].ParameterType);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }

                var task = method.Invoke(_client, args) as Task;
                if (task != null)
                {
                    await task;
                    // Try to get the result
                    var resultProp = task.GetType().GetProperty("Result");
                    var result = resultProp?.GetValue(task);
                    if (result is IEnumerable<object> items)
                    {
                        var models = new List<string>();
                        foreach (var item in items)
                        {
                            // Try common property names: Id, Name, ModelId
                            var id = item.GetType().GetProperty("Id")?.GetValue(item)?.ToString()
                                  ?? item.GetType().GetProperty("ModelId")?.GetValue(item)?.ToString()
                                  ?? item.GetType().GetProperty("Name")?.GetValue(item)?.ToString()
                                  ?? item.ToString();
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                models.Add(id);
                            }
                        }
                        return models;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ListModelsAsync not available or failed");
        }

        return null;
    }
}
