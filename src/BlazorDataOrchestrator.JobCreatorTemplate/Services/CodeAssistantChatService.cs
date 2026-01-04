using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using Azure.AI.OpenAI;
using Radzen;
using Radzen.Blazor;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using RadzenChatMessage = Radzen.Blazor.ChatMessage;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services;

/// <summary>
/// AI Chat service for code assistance using Microsoft.Extensions.AI.
/// Supports OpenAI and Azure OpenAI services.
/// </summary>
public class CodeAssistantChatService : IAIChatService
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    private readonly AISettingsService _settingsService;
    private AISettings? _cachedSettings;
    private IChatClient? _chatClient;
    
    private const string DefaultSystemPrompt = @"You are a helpful code assistant specializing in Python and C# development. 
You help developers with:
- Writing and debugging code
- Explaining programming concepts
- Best practices and code optimization
- Understanding libraries and frameworks
Keep responses concise and focused on the code task at hand.";

    public CodeAssistantChatService(AISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    private async Task<IChatClient?> GetOrCreateChatClientAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        
        // Check if settings changed
        if (_cachedSettings != null && 
            _cachedSettings.AIServiceType == settings.AIServiceType &&
            _cachedSettings.ApiKey == settings.ApiKey &&
            _cachedSettings.AIModel == settings.AIModel &&
            _cachedSettings.Endpoint == settings.Endpoint)
        {
            return _chatClient;
        }

        _cachedSettings = settings;

        if (!settings.IsConfigured)
        {
            _chatClient = null;
            return null;
        }

        try
        {
            if (settings.AIServiceType == "Azure OpenAI")
            {
                var azureClient = new AzureOpenAIClient(
                    new Uri(settings.Endpoint),
                    new ApiKeyCredential(settings.ApiKey));
                _chatClient = azureClient.GetChatClient(settings.AIModel).AsIChatClient();
            }
            else // OpenAI
            {
                var openAIClient = new OpenAIClient(new ApiKeyCredential(settings.ApiKey));
                _chatClient = openAIClient.GetChatClient(settings.AIModel).AsIChatClient();
            }
        }
        catch (Exception)
        {
            _chatClient = null;
        }

        return _chatClient;
    }

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
        var session = GetOrCreateSession(sessionId);
        session.Messages.Add(new RadzenChatMessage { IsUser = true, Content = userInput });

        var chatClient = await GetOrCreateChatClientAsync();
        
        if (chatClient == null)
        {
            var fallbackResponse = "⚠️ AI service is not configured. Please click the gear icon (⚙️) to configure your OpenAI or Azure OpenAI settings.";
            session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = fallbackResponse });
            yield return fallbackResponse;
            yield break;
        }

        // Process the AI request and collect results
        var result = await ProcessAIRequestAsync(session, systemPrompt, temperature, maxTokens, cancellationToken);
        
        foreach (var chunk in result)
        {
            yield return chunk;
        }
    }

    private async Task<List<string>> ProcessAIRequestAsync(
        ConversationSession session,
        string? systemPrompt,
        double? temperature,
        int? maxTokens,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        
        try
        {
            // Build conversation history for context
            var messages = new List<AIChatMessage>
            {
                new(ChatRole.System, systemPrompt ?? DefaultSystemPrompt)
            };

            // Add conversation history (limit to last 10 messages for context window)
            var recentMessages = session.Messages.TakeLast(10);
            foreach (var msg in recentMessages)
            {
                messages.Add(new AIChatMessage(
                    msg.IsUser ? ChatRole.User : ChatRole.Assistant,
                    msg.Content));
            }

            var options = new ChatOptions
            {
                Temperature = (float?)temperature ?? 0.7f,
                MaxOutputTokens = maxTokens ?? 2048
            };

            var responseBuilder = new System.Text.StringBuilder();
            
            await foreach (var update in _chatClient!.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                if (update.Text != null)
                {
                    responseBuilder.Append(update.Text);
                    results.Add(update.Text);
                }
            }

            // Store complete response in session
            session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = responseBuilder.ToString() });
        }
        catch (Exception ex)
        {
            var errorMessage = $"❌ Error communicating with AI service: {ex.Message}";
            session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = errorMessage });
            results.Clear();
            results.Add(errorMessage);
        }

        return results;
    }

    public ConversationSession GetOrCreateSession(string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString();
        
        return _sessions.GetOrAdd(sessionId, id => new ConversationSession
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            Messages = new List<RadzenChatMessage>()
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

    /// <summary>
    /// Refreshes the chat client when settings are updated.
    /// </summary>
    public void RefreshClient()
    {
        _cachedSettings = null;
        _chatClient = null;
    }
}
