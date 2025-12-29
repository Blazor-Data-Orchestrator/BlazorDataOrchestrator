using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Radzen;
using Radzen.Blazor;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services;

/// <summary>
/// A simple AI Chat service for code assistance.
/// This can be extended to integrate with actual AI services like OpenAI, Azure OpenAI, etc.
/// </summary>
public class CodeAssistantChatService : IAIChatService
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    
    private readonly Dictionary<string, string> _codeHelpers = new()
    {
        { "python", "I can help you with Python code! Ask me about syntax, libraries, or best practices." },
        { "csharp", "I can help you with C# code! Ask me about syntax, .NET libraries, or best practices." },
        { "c#", "I can help you with C# code! Ask me about syntax, .NET libraries, or best practices." },
        { "help", "I'm your code assistant! I can help with:\n• Python programming\n• C# and .NET development\n• Code debugging\n• Best practices" },
        { "hello", "Hello! I'm your code assistant. How can I help you today?" },
        { "hi", "Hi there! I'm ready to help you with your code. What would you like to know?" }
    };

    public async IAsyncEnumerable<string> GetCompletionsAsync(
        string userInput, 
        string? sessionId = null, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default, 
        string? model = null,
        string? systemPrompt = null, 
        double? temperature = null, 
        int? maxTokens = null,
        string? endpoint = null, 
        string? proxy = null, 
        string? apiKey = null, 
        string? apiKeyHeader = null)
    {
        // Get or create the session
        var session = GetOrCreateSession(sessionId);
        
        // Add user message to history
        session.Messages.Add(new ChatMessage { IsUser = true, Content = userInput });
        
        // Simulate typing delay for more natural feel
        await Task.Delay(100, cancellationToken);

        var lowerMessage = userInput.ToLowerInvariant();
        string response;

        // Check for matching keywords
        var matchedKey = _codeHelpers.Keys.FirstOrDefault(k => lowerMessage.Contains(k));
        
        if (matchedKey != null)
        {
            response = _codeHelpers[matchedKey];
        }
        else if (lowerMessage.Contains("error") || lowerMessage.Contains("bug") || lowerMessage.Contains("fix"))
        {
            response = "I'd be happy to help debug your code! Please share the error message or describe the issue you're experiencing.";
        }
        else if (lowerMessage.Contains("function") || lowerMessage.Contains("method") || lowerMessage.Contains("class"))
        {
            response = "I can help you understand or write functions/classes. What specific functionality are you trying to implement?";
        }
        else
        {
            response = $"I received your message: \"{userInput}\"\n\nI'm a code assistant that can help with Python and C# development. Try asking about specific programming concepts!";
        }

        // Add assistant response to history
        session.Messages.Add(new ChatMessage { IsUser = false, Content = response });

        // Stream the response character by character for a typing effect
        foreach (var chunk in SplitIntoChunks(response, 5))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
                
            yield return chunk;
            await Task.Delay(20, cancellationToken);
        }
    }

    public ConversationSession GetOrCreateSession(string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString();
        
        return _sessions.GetOrAdd(sessionId, id => new ConversationSession
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            Messages = new List<ChatMessage>()
        });
    }

    public void ClearSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Messages.Clear();
        }
    }

    public IEnumerable<ConversationSession> GetActiveSessions()
    {
        return _sessions.Values.ToList();
    }

    public void CleanupOldSessions(int maxAgeHours = 24)
    {
        var cutoff = DateTime.UtcNow.AddHours(-maxAgeHours);
        var oldSessions = _sessions.Where(kvp => kvp.Value.CreatedAt < cutoff).Select(kvp => kvp.Key).ToList();
        
        foreach (var sessionId in oldSessions)
        {
            _sessions.TryRemove(sessionId, out _);
        }
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize)
    {
        for (int i = 0; i < text.Length; i += chunkSize)
        {
            yield return text.Substring(i, Math.Min(chunkSize, text.Length - i));
        }
    }
}
