using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Interface for AI chat services supporting code assistance.
/// </summary>
public interface IAIChatService
{
    /// <summary>
    /// Sets the current code from the editor to be included in AI requests.
    /// </summary>
    void SetCurrentEditorCode(string code);
    
    /// <summary>
    /// Sets the current programming language context.
    /// </summary>
    void SetLanguage(string language);
    
    /// <summary>
    /// Gets streaming completions from the AI service.
    /// </summary>
    IAsyncEnumerable<string> GetCompletionsAsync(
        string userInput,
        string? sessionId = null,
        CancellationToken cancellationToken = default,
        string? model = null,
        string? systemPrompt = null,
        double? temperature = null,
        int? maxTokens = null,
        string? endpoint = null,
        string? proxy = null,
        string? apiKey = null,
        string? apiKeyHeader = null);
    
    /// <summary>
    /// Gets or creates a conversation session.
    /// </summary>
    ConversationSession GetOrCreateSession(string? sessionId = null);
    
    /// <summary>
    /// Clears a conversation session.
    /// </summary>
    void ClearSession(string sessionId);
    
    /// <summary>
    /// Refreshes the chat client when settings change.
    /// </summary>
    void RefreshClient();
}
