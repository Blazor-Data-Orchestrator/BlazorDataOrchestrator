using GitHub.Copilot.SDK;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services;

/// <summary>
/// Discovers and caches the list of Copilot models available to the current user.
/// Falls back to a hardcoded baseline when the API is unreachable.
/// </summary>
public class CopilotModelService
{
    private readonly CopilotClient _client;
    private readonly ILogger<CopilotModelService> _logger;

    private List<string>? _cachedModels;
    private DateTime? _lastRefreshed;

    /// <summary>
    /// Hardcoded fallback models used when the API cannot be reached.
    /// </summary>
    private static readonly List<string> FallbackModels = new()
    {
        "gpt-4.1",
        "gpt-4.1-mini",
        "gpt-5",
        "gpt-5.2",
        "claude-sonnet-4.5",
        "o1",
        "o1-mini",
        "o3-mini"
    };

    public CopilotModelService(CopilotClient client, ILogger<CopilotModelService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Timestamp of the last successful model refresh, if any.
    /// </summary>
    public DateTime? LastRefreshed => _lastRefreshed;

    /// <summary>
    /// Returns the list of available models. Tries the SDK first, then falls back.
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedModels != null)
        {
            return _cachedModels;
        }

        try
        {
            if (_client.State == ConnectionState.Connected)
            {
                var models = await FetchModelsFromSdkAsync();
                if (models != null && models.Count > 0)
                {
                    // Merge with fallback to ensure baseline models are always available
                    var merged = new HashSet<string>(models, StringComparer.OrdinalIgnoreCase);
                    foreach (var fallback in FallbackModels)
                    {
                        merged.Add(fallback);
                    }

                    _cachedModels = merged.OrderBy(m => m).ToList();
                    _lastRefreshed = DateTime.UtcNow;
                    _logger.LogInformation("Fetched {Count} models from Copilot API", models.Count);
                    return _cachedModels;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch models from Copilot API, using fallback list");
        }

        // Fallback
        _cachedModels = new List<string>(FallbackModels);
        _lastRefreshed = null;
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
