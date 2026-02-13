using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using OpenAI;
using Azure.AI.OpenAI;
using System.ClientModel;
using Mscc.GenerativeAI;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Service for fetching AI models from provider APIs and caching them in Azure Table Storage.
/// Models are cached with a 24-hour TTL to avoid repeated API calls.
/// </summary>
public class AIModelCacheService
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<AIModelCacheService> _logger;
    private const string TableName = "AIModelCache";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    // Well-known Anthropic models (Anthropic has no public list-models API)
    private static readonly List<string> KnownAnthropicModels = new()
    {
        "claude-sonnet-4-20250514",
        "claude-opus-4-20250514",
        "claude-3-7-sonnet-latest",
        "claude-3-5-sonnet-latest",
        "claude-3-5-haiku-latest",
        "claude-3-opus-latest",
        "claude-3-haiku-20240307"
    };

    public AIModelCacheService(TableServiceClient tableServiceClient, ILogger<AIModelCacheService> logger)
    {
        _tableServiceClient = tableServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the list of available models for the specified provider.
    /// Returns cached models if available and not expired; otherwise fetches from the API.
    /// </summary>
    public async Task<List<string>> GetModelsAsync(string serviceType, string apiKey, string? endpoint = null, string? apiVersion = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return GetDefaultModels(serviceType);

        // Try to get from cache first
        var cached = await GetCachedModelsAsync(serviceType, apiKey);
        if (cached != null)
            return cached;

        // Fetch from API
        var models = await FetchModelsFromApiAsync(serviceType, apiKey, endpoint, apiVersion);

        // Cache the results
        if (models.Count > 0)
        {
            await CacheModelsAsync(serviceType, apiKey, models);
        }

        return models;
    }

    /// <summary>
    /// Forces a refresh of models from the provider API, ignoring the cache.
    /// </summary>
    public async Task<List<string>> RefreshModelsAsync(string serviceType, string apiKey, string? endpoint = null, string? apiVersion = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return GetDefaultModels(serviceType);

        var models = await FetchModelsFromApiAsync(serviceType, apiKey, endpoint, apiVersion);

        if (models.Count > 0)
        {
            await CacheModelsAsync(serviceType, apiKey, models);
        }

        return models;
    }

    /// <summary>
    /// Returns default models for a given provider (used as fallback when API key is not set).
    /// </summary>
    public static List<string> GetDefaultModels(string serviceType)
    {
        return serviceType switch
        {
            "OpenAI" => new List<string>
            {
                "gpt-4.1",
                "gpt-4.1-mini",
                "gpt-4.1-nano",
                "gpt-4o",
                "gpt-4o-mini",
                "gpt-4-turbo-preview",
                "gpt-4-turbo",
                "gpt-4",
                "o4-mini",
                "o3",
                "o3-mini",
                "o1",
                "o1-mini"
            },
            "Azure OpenAI" => new List<string>
            {
                "gpt-4o",
                "gpt-4o-mini",
                "gpt-4-turbo",
                "gpt-4",
                "gpt-35-turbo"
            },
            "Anthropic" => new List<string>(KnownAnthropicModels),
            "Google AI" => new List<string>
            {
                "gemini-2.5-pro-preview-06-05",
                "gemini-2.5-flash-preview-05-20",
                "gemini-2.0-flash",
                "gemini-2.0-flash-lite",
                "gemini-1.5-pro",
                "gemini-1.5-flash",
                "gemini-1.5-flash-8b"
            },
            _ => new List<string>()
        };
    }

    private async Task<List<string>> FetchModelsFromApiAsync(string serviceType, string apiKey, string? endpoint, string? apiVersion)
    {
        try
        {
            return serviceType switch
            {
                "OpenAI" => await FetchOpenAIModelsAsync(apiKey),
                "Azure OpenAI" => await FetchAzureOpenAIModelsAsync(apiKey, endpoint, apiVersion),
                "Anthropic" => await Task.FromResult(new List<string>(KnownAnthropicModels)),
                "Google AI" => await FetchGoogleAIModelsAsync(apiKey),
                _ => new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch models from {ServiceType} API. Returning defaults.", serviceType);
            return GetDefaultModels(serviceType);
        }
    }

    private async Task<List<string>> FetchOpenAIModelsAsync(string apiKey)
    {
        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        var modelClient = client.GetOpenAIModelClient();
        var response = await modelClient.GetModelsAsync();

        var models = response.Value
            .Where(m => m.Id.StartsWith("gpt-") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3") || m.Id.StartsWith("o4"))
            .Where(m => !m.Id.Contains("instruct") && !m.Id.Contains("realtime") && !m.Id.Contains("audio"))
            .Select(m => m.Id)
            .OrderByDescending(m => m)
            .Distinct()
            .ToList();

        return models.Count > 0 ? models : GetDefaultModels("OpenAI");
    }

    private async Task<List<string>> FetchAzureOpenAIModelsAsync(string apiKey, string? endpoint, string? apiVersion)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return GetDefaultModels("Azure OpenAI");

        var version = string.IsNullOrWhiteSpace(apiVersion) ? "2024-06-01" : apiVersion;
        var url = $"{endpoint.TrimEnd('/')}/openai/models?api-version={version}";

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Azure OpenAI models API returned {StatusCode}. Trying deployments endpoint.", response.StatusCode);
            return await FetchAzureOpenAIDeploymentsAsync(apiKey, endpoint, version);
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var model in data.EnumerateArray())
            {
                if (model.TryGetProperty("id", out var id))
                {
                    models.Add(id.GetString()!);
                }
            }
        }

        return models.Count > 0 ? models.OrderBy(m => m).ToList() : GetDefaultModels("Azure OpenAI");
    }

    private async Task<List<string>> FetchAzureOpenAIDeploymentsAsync(string apiKey, string endpoint, string apiVersion)
    {
        var url = $"{endpoint.TrimEnd('/')}/openai/deployments?api-version={apiVersion}";

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

        var response = await httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return GetDefaultModels("Azure OpenAI");

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var deployments = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var deployment in data.EnumerateArray())
            {
                if (deployment.TryGetProperty("id", out var id))
                {
                    deployments.Add(id.GetString()!);
                }
            }
        }

        return deployments.Count > 0 ? deployments.OrderBy(d => d).ToList() : GetDefaultModels("Azure OpenAI");
    }

    private async Task<List<string>> FetchGoogleAIModelsAsync(string apiKey)
    {
        var googleAI = new GoogleAI(apiKey);
        var generativeModel = googleAI.GenerativeModel("gemini-1.5-pro");

        var response = await generativeModel.ListModels();

        var models = response
            .Where(m => m.Name != null && m.Name.Contains("gemini"))
            .Where(m => m.SupportedGenerationMethods?.Any(g => g.Contains("generateContent")) == true)
            .Select(m => m.Name!.Replace("models/", ""))
            .Where(m => !m.Contains("embedding") && !m.Contains("aqa") && !m.Contains("imagen"))
            .OrderByDescending(m => m)
            .Distinct()
            .ToList();

        return models.Count > 0 ? models : GetDefaultModels("Google AI");
    }

    #region Azure Table Storage Cache

    private async Task<List<string>?> GetCachedModelsAsync(string serviceType, string apiKey)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync();

            // Use a hash of the API key so we don't store keys as row keys
            var rowKey = GetCacheRowKey(apiKey);
            var response = await tableClient.GetEntityIfExistsAsync<AIModelCacheEntity>(serviceType, rowKey);

            if (response.HasValue && response.Value != null)
            {
                var entity = response.Value;

                // Check TTL
                if (entity.LastFetched.HasValue &&
                    DateTimeOffset.UtcNow - entity.LastFetched.Value < CacheTtl)
                {
                    var models = JsonSerializer.Deserialize<List<string>>(entity.ModelsJson ?? "[]");
                    if (models != null && models.Count > 0)
                        return models;
                }
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Failed to read model cache for {ServiceType}.", serviceType);
        }

        return null;
    }

    private async Task CacheModelsAsync(string serviceType, string apiKey, List<string> models)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync();

            var entity = new AIModelCacheEntity
            {
                PartitionKey = serviceType,
                RowKey = GetCacheRowKey(apiKey),
                ModelsJson = JsonSerializer.Serialize(models),
                LastFetched = DateTimeOffset.UtcNow
            };

            await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache models for {ServiceType}.", serviceType);
        }
    }

    private static string GetCacheRowKey(string apiKey)
    {
        // Trim to avoid duplicate entries from whitespace variations in the key
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(apiKey.Trim()));
        return Convert.ToHexString(hash)[..16];
    }

    #endregion
}

/// <summary>
/// Azure Table entity for caching AI model lists.
/// </summary>
public class AIModelCacheEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// JSON-serialized list of model IDs.
    /// </summary>
    public string? ModelsJson { get; set; }

    /// <summary>
    /// When the models were last fetched from the provider API.
    /// </summary>
    public DateTimeOffset? LastFetched { get; set; }
}
