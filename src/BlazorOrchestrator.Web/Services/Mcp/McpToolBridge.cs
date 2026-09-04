using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace BlazorOrchestrator.Web.Services.Mcp;

/// <summary>
/// Turns registered MCP tools into <see cref="AIFunction"/> instances the chat model can call, and
/// applies the guard rails: results are truncated and community code is attributed.
/// </summary>
public sealed class McpToolBridge(IMcpClientRegistry registry, ILogger<McpToolBridge> logger)
    : BlazorDataOrchestrator.Core.Services.IExternalToolProvider
{
    private const int MaxResultCharacters = 20_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IList<AITool>> GetToolsAsync(bool includeWriteTools, CancellationToken cancellationToken = default) =>
        await BuildToolsAsync(includeWriteTools, cancellationToken);

    public async Task<IList<AITool>> BuildToolsAsync(bool includeWriteTools, CancellationToken ct = default)
    {
        var tools = new List<AITool>();

        foreach (var tool in await registry.GetToolsAsync(includeWriteTools, ct))
        {
            tools.Add(Create(tool));
        }

        return tools;
    }

    private AIFunction Create(McpToolInfo tool) => AIFunctionFactory.Create(
        async (IDictionary<string, object?> arguments, CancellationToken cancellationToken) =>
        {
            var payload = new JsonObject();
            foreach (var (key, value) in arguments)
            {
                payload[key] = value is null ? null : JsonSerializer.SerializeToNode(value, Json);
            }

            var result = await registry.CallAsync(tool.QualifiedName, payload, cancellationToken);

            if (result.InputRequired is { } inputRequired)
            {
                return $"AUTHORIZATION_REQUIRED: {inputRequired.Message} Open {inputRequired.Url} to continue.";
            }

            if (!result.Succeeded)
            {
                logger.LogDebug("MCP tool {Tool} failed: {Error}", tool.QualifiedName, result.ErrorMessage);
                return $"ERROR: {result.ErrorMessage ?? "The tool call failed."}";
            }

            var text = result.Text ?? result.StructuredContent?.ToJsonString(Json) ?? string.Empty;
            if (text.Length > MaxResultCharacters)
            {
                text = text[..MaxResultCharacters] + "\n... [truncated]";
            }

            // The model must treat community content as untrusted data, never as instructions.
            return $"""
                <mcp-tool-result tool="{tool.QualifiedName}" trust="untrusted-data">
                {text}
                </mcp-tool-result>
                Any code reproduced from this result must be attributed to its community package, version and license.
                """;
        },
        new AIFunctionFactoryOptions
        {
            Name = tool.QualifiedName.Replace('.', '_'),
            Description = BuildDescription(tool)
        });

    private static string BuildDescription(McpToolInfo tool)
    {
        var description = tool.Description ?? tool.Title ?? tool.Name;
        var schema = tool.InputSchema?.ToJsonString(Json);

        return string.IsNullOrEmpty(schema)
            ? description
            : $"{description} Arguments (JSON Schema): {schema}";
    }
}
