using Radzen.Blazor;

namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// Represents an AI chat conversation session.
/// </summary>
public class ConversationSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = new();
}
