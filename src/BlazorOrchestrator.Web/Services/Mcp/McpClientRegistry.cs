using System.Text.Json;
using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Web.Services.Community;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorOrchestrator.Web.Services.Mcp;

public interface IMcpClientRegistry
{
    Task<IReadOnlyList<McpServerDescriptor>> GetServersAsync(CancellationToken ct = default);
    Task SaveServersAsync(IEnumerable<McpServerDescriptor> servers, CancellationToken ct = default);
    Task<IReadOnlyList<McpToolInfo>> GetToolsAsync(bool includeWriteTools, CancellationToken ct = default);
    Task<McpToolResult> CallAsync(string qualifiedToolName, System.Text.Json.Nodes.JsonObject arguments, CancellationToken ct = default);
    Task<McpServerIdentity?> TestAsync(McpServerDescriptor server, CancellationToken ct = default);
}

/// <summary>
/// Holds the registered MCP servers. The Community Jobs Library is pre-registered and always present;
/// administrators may add additional servers, which are disabled by default.
/// </summary>
public sealed class McpClientRegistry(
    IMcpClient mcpClient,
    ICommunitySettingsService communitySettings,
    SettingsService settingsService,
    IMemoryCache cache,
    ILogger<McpClientRegistry> logger) : IMcpClientRegistry
{
    private const string ServersSettingKey = "Mcp:Servers";
    private const string ToolCacheKey = "mcp:tools";
    private static readonly TimeSpan ToolCacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<McpServerDescriptor>> GetServersAsync(CancellationToken ct = default)
    {
        var options = await communitySettings.GetOptionsAsync(ct);

        var community = new McpServerDescriptor
        {
            Name = McpServerDescriptor.CommunityServerName,
            Url = options.McpEndpoint,
            AuthMode = McpAuthMode.OAuth,
            Enabled = options.EnableCommunityBrowse
        };

        var servers = new List<McpServerDescriptor> { community };
        servers.AddRange(await ReadAdditionalServersAsync());
        return servers;
    }

    public async Task SaveServersAsync(IEnumerable<McpServerDescriptor> servers, CancellationToken ct = default)
    {
        var additional = servers
            .Where(s => !string.Equals(s.Name, McpServerDescriptor.CommunityServerName, StringComparison.Ordinal))
            .ToList();

        await settingsService.SetAsync(ServersSettingKey, JsonSerializer.Serialize(additional, Json),
            "Additional MCP servers available to the AI assistant");

        cache.Remove(ToolCacheKey);
    }

    public async Task<IReadOnlyList<McpToolInfo>> GetToolsAsync(bool includeWriteTools, CancellationToken ct = default)
    {
        var cacheKey = $"{ToolCacheKey}:{includeWriteTools}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<McpToolInfo>? cached) && cached is not null)
        {
            return cached;
        }

        var tools = new List<McpToolInfo>();

        foreach (var server in await GetServersAsync(ct))
        {
            if (!server.Enabled)
            {
                continue;
            }

            foreach (var tool in await mcpClient.ListToolsAsync(server, ct))
            {
                if (!tool.ReadOnly && !(includeWriteTools && server.AllowWriteTools))
                {
                    continue;
                }

                if (server.AllowedToolPrefixes.Count > 0 &&
                    !server.AllowedToolPrefixes.Any(p => tool.Name.StartsWith(p, StringComparison.Ordinal)))
                {
                    continue;
                }

                tools.Add(tool);
            }
        }

        // Deterministic ordering keeps prompt caches warm across turns.
        var ordered = tools.OrderBy(t => t.QualifiedName, StringComparer.Ordinal).ToList();
        cache.Set(cacheKey, (IReadOnlyList<McpToolInfo>)ordered, ToolCacheLifetime);
        return ordered;
    }

    public async Task<McpToolResult> CallAsync(
        string qualifiedToolName, System.Text.Json.Nodes.JsonObject arguments, CancellationToken ct = default)
    {
        var separator = qualifiedToolName.IndexOf('.');
        if (separator <= 0)
        {
            return new McpToolResult(false, null, null, $"'{qualifiedToolName}' is not a namespaced MCP tool name.", null);
        }

        var serverName = qualifiedToolName[..separator];
        var toolName = qualifiedToolName[(separator + 1)..];

        var server = (await GetServersAsync(ct))
            .FirstOrDefault(s => s.Enabled && string.Equals(s.Name, serverName, StringComparison.Ordinal));

        if (server is null)
        {
            return new McpToolResult(false, null, null, $"No enabled MCP server named '{serverName}'.", null);
        }

        return await mcpClient.CallToolAsync(server, toolName, arguments, ct);
    }

    public Task<McpServerIdentity?> TestAsync(McpServerDescriptor server, CancellationToken ct = default) =>
        mcpClient.DiscoverAsync(server, ct);

    private async Task<List<McpServerDescriptor>> ReadAdditionalServersAsync()
    {
        var raw = await settingsService.GetAsync(ServersSettingKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<McpServerDescriptor>>(raw, Json) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The stored MCP server list could not be parsed and was ignored");
            return [];
        }
    }
}
