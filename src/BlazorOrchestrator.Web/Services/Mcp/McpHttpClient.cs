using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazorOrchestrator.Web.Services.Mcp;

public interface IMcpClient
{
    Task<McpServerIdentity?> DiscoverAsync(McpServerDescriptor server, CancellationToken ct = default);
    Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerDescriptor server, CancellationToken ct = default);
    Task<McpToolResult> CallToolAsync(McpServerDescriptor server, string toolName, JsonObject arguments, CancellationToken ct = default);
}

/// <summary>
/// Stateless Streamable HTTP client for MCP specification revision 2026-07-28. There is no initialize
/// handshake: the protocol version, client capabilities and client info travel in <c>_meta</c> on every
/// request, and the Mcp-Method / Mcp-Name headers mirror the JSON-RPC body.
/// </summary>
public sealed class McpHttpClient(
    IHttpClientFactory httpClientFactory,
    IMcpCredentialProvider credentialProvider,
    ILogger<McpHttpClient> logger) : IMcpClient
{
    public const string HttpClientName = "mcp";
    public const string ProtocolVersion = "2026-07-28";

    private const string MetaProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    private const string MetaClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    private const string MetaClientInfo = "io.modelcontextprotocol/clientInfo";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<McpServerIdentity?> DiscoverAsync(McpServerDescriptor server, CancellationToken ct = default)
    {
        var response = await SendAsync(server, "server/discover", null, name: null, ct);
        if (response is null || response["result"] is not JsonObject result)
        {
            return null;
        }

        var info = result["serverInfo"] as JsonObject;
        var versions = (result["protocolVersions"] as JsonArray)?
            .Select(v => v?.GetValue<string>() ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList() ?? [];

        return new McpServerIdentity(
            info?["name"]?.GetValue<string>() ?? server.Name,
            info?["version"]?.GetValue<string>() ?? "unknown",
            versions);
    }

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerDescriptor server, CancellationToken ct = default)
    {
        var response = await SendAsync(server, "tools/list", null, name: null, ct);
        if (response is null || response["result"] is not JsonObject result || result["tools"] is not JsonArray tools)
        {
            return [];
        }

        var list = new List<McpToolInfo>(tools.Count);

        foreach (var node in tools.OfType<JsonObject>())
        {
            var name = node["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var annotations = node["annotations"] as JsonObject;

            list.Add(new McpToolInfo
            {
                ServerName = server.Name,
                Name = name,
                Title = node["title"]?.GetValue<string>(),
                Description = node["description"]?.GetValue<string>(),
                InputSchema = node["inputSchema"]?.DeepClone() as JsonObject,
                ReadOnly = annotations?["readOnlyHint"]?.GetValue<bool>() ?? true,
                RequiredScope = annotations?["requiredScope"]?.GetValue<string>()
            });
        }

        return list;
    }

    public async Task<McpToolResult> CallToolAsync(
        McpServerDescriptor server, string toolName, JsonObject arguments, CancellationToken ct = default)
    {
        var parameters = new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments
        };

        var response = await SendAsync(server, "tools/call", parameters, toolName, ct);
        if (response is null)
        {
            return new McpToolResult(false, null, null, $"The MCP server '{server.Name}' is unreachable.", null);
        }

        if (response["error"] is JsonObject error)
        {
            return new McpToolResult(false, null, null,
                error["message"]?.GetValue<string>() ?? "The tool call failed.", null);
        }

        if (response["result"] is not JsonObject result)
        {
            return new McpToolResult(false, null, null, "The tool returned no result.", null);
        }

        // Multi Round-Trip Requests: the server needs out-of-band input before it can proceed.
        if (result["resultType"]?.GetValue<string>() == "input_required")
        {
            var request = (result["inputRequests"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            return new McpToolResult(false, null, null, null, new McpInputRequired(
                request?["url"]?.GetValue<string>() ?? string.Empty,
                request?["message"]?.GetValue<string>() ?? "Additional authorization is required.",
                result["requestState"]?.GetValue<string>() ?? string.Empty));
        }

        var text = (result["content"] as JsonArray)?
            .OfType<JsonObject>()
            .Where(c => c["type"]?.GetValue<string>() == "text")
            .Select(c => c["text"]?.GetValue<string>())
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));

        return new McpToolResult(true, text, result["structuredContent"]?.DeepClone(), null, null);
    }

    // ---------------------------------------------------------------- transport

    private async Task<JsonObject?> SendAsync(
        McpServerDescriptor server, string method, JsonObject? parameters, string? name, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString("N"),
            ["method"] = method,
            ["_meta"] = new JsonObject
            {
                [MetaProtocolVersion] = ProtocolVersion,
                [MetaClientCapabilities] = new JsonObject
                {
                    ["extensions"] = new JsonObject()
                },
                [MetaClientInfo] = new JsonObject
                {
                    ["name"] = "blazor-data-orchestrator",
                    ["version"] = typeof(McpHttpClient).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                }
            }
        };

        if (parameters is not null)
        {
            body["params"] = parameters;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
        {
            Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        if (!string.IsNullOrEmpty(name))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Name", name);
        }

        var credential = await credentialProvider.GetCredentialAsync(server, ct);
        if (credential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);

            var payload = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonNode.Parse(payload) as JsonObject;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "The MCP call {Method} to {Server} failed", method, server.Name);
            return null;
        }
    }
}

public interface IMcpCredentialProvider
{
    Task<string?> GetCredentialAsync(McpServerDescriptor server, CancellationToken ct = default);
}

/// <summary>Supplies the community OAuth token for the built-in server and static keys for the rest.</summary>
public sealed class McpCredentialProvider(
    Services.Community.ICommunityAuthService authService,
    Services.Community.ICommunityUserContext userContext) : IMcpCredentialProvider
{
    public async Task<string?> GetCredentialAsync(McpServerDescriptor server, CancellationToken ct = default)
    {
        if (server.AuthMode == McpAuthMode.ApiKey)
        {
            return server.ApiKey;
        }

        if (server.AuthMode != McpAuthMode.OAuth)
        {
            return null;
        }

        var userId = await userContext.GetUserIdAsync(ct);
        return userId is null ? null : await authService.GetValidAccessTokenAsync(userId, ct);
    }
}
