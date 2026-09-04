using Microsoft.Extensions.AI;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Supplies tools from outside the Core assembly (currently MCP servers) to the AI chat service.
/// Implementations must treat tool output as untrusted data rather than instructions.
/// </summary>
public interface IExternalToolProvider
{
    Task<IList<AITool>> GetToolsAsync(bool includeWriteTools, CancellationToken cancellationToken = default);
}
