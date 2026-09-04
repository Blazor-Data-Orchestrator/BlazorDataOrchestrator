using System.Text.Json.Nodes;

namespace BlazorOrchestrator.Web.Services.Mcp;

/// <summary>Authentication mode used when talking to a registered MCP server.</summary>
public enum McpAuthMode
{
    None = 0,
    OAuth = 1,
    ApiKey = 2
}

public sealed record McpServerDescriptor
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public McpAuthMode AuthMode { get; init; } = McpAuthMode.None;
    public string? ApiKey { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>When populated, only tools whose names start with one of these prefixes are exposed.</summary>
    public IReadOnlyList<string> AllowedToolPrefixes { get; init; } = [];

    /// <summary>Write tools are hidden from the model unless the user opts in for the conversation.</summary>
    public bool AllowWriteTools { get; init; }

    public const string CommunityServerName = "cjl";
}

public sealed record McpToolInfo
{
    public required string ServerName { get; init; }
    public required string Name { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public JsonObject? InputSchema { get; init; }
    public bool ReadOnly { get; init; } = true;
    public string? RequiredScope { get; init; }

    /// <summary>Namespaced name presented to the model, for example <c>cjl.search_packages</c>.</summary>
    public string QualifiedName => $"{ServerName}.{Name}";
}

public sealed record McpToolResult(
    bool Succeeded,
    string? Text,
    JsonNode? StructuredContent,
    string? ErrorMessage,
    McpInputRequired? InputRequired);

public sealed record McpInputRequired(string Url, string Message, string RequestState);

public sealed record McpServerIdentity(string Name, string Version, IReadOnlyList<string> ProtocolVersions);
