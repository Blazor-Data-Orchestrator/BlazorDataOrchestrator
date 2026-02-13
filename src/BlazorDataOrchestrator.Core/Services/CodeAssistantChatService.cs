using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using Azure.AI.OpenAI;
using BlazorDataOrchestrator.Core.Models;

// Use aliases to avoid ambiguity
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using RadzenChatMessage = Radzen.Blazor.ChatMessage;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// AI Chat service for code assistance using Microsoft.Extensions.AI.
/// Supports OpenAI and Azure OpenAI services.
/// </summary>
public class CodeAssistantChatService : IAIChatService
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    private readonly AISettingsService _settingsService;
    private readonly IInstructionsProvider _instructionsProvider;
    private AISettings? _cachedSettings;
    private IChatClient? _chatClient;
    private string _currentEditorCode = "";
    private string _currentLanguage = "csharp";
    
    private const string BaseSystemPrompt = @"You are a helpful code assistant specializing in Python and C# development. 
You help developers with:
- Writing and debugging code
- Explaining programming concepts
- Best practices and code optimization
- Understanding libraries and frameworks
Keep responses concise and focused on the code task at hand.

## Response Formatting Rules
- When providing code snippets or examples, ALWAYS wrap them in markdown fenced code blocks using triple backticks with the language identifier (e.g. ```csharp or ```python).
- When the response is a code update or a complete/modified version of the user's code, you MUST surround the full code with the markers ###UPDATED CODE BEGIN### and ###UPDATED CODE END### so the system can offer an 'Apply to Editor' action.
- Place the fenced code block INSIDE the markers. Example:
###UPDATED CODE BEGIN###
```csharp
// full updated code here
```
###UPDATED CODE END###
- NEVER return code outside of fenced code blocks.";

    public CodeAssistantChatService(
        AISettingsService settingsService, 
        IInstructionsProvider instructionsProvider)
    {
        _settingsService = settingsService;
        _instructionsProvider = instructionsProvider;
    }
    
    /// <summary>
    /// Sets the current code from the editor to be included in AI requests.
    /// </summary>
    public void SetCurrentEditorCode(string code)
    {
        _currentEditorCode = code ?? "";
    }
    
    /// <summary>
    /// Sets the current programming language context.
    /// </summary>
    public void SetLanguage(string language)
    {
        _currentLanguage = language ?? "csharp";
    }
    
    /// <summary>
    /// Builds the dynamic system prompt with language-specific instructions.
    /// </summary>
    private string BuildSystemPrompt(bool isNewSession)
    {
        var promptBuilder = new System.Text.StringBuilder();
        promptBuilder.AppendLine(BaseSystemPrompt);
        
        // Always include language-specific instructions so the AI consistently
        // follows the code markers convention and project-specific constraints.
        var instructions = _instructionsProvider.GetInstructionsForLanguage(_currentLanguage);
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("## Custom Instructions for Code Generation");
            promptBuilder.AppendLine(instructions);
        }
        
        return promptBuilder.ToString();
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
            else if (settings.AIServiceType == "Anthropic")
            {
                _chatClient = new AnthropicChatClientAdapter(settings.ApiKey, settings.AIModel);
            }
            else if (settings.AIServiceType == "Google AI")
            {
                _chatClient = new GoogleAIChatClientAdapter(settings.ApiKey, settings.AIModel);
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
            var fallbackResponse = "⚠️ AI service is not configured. Please click the gear icon (⚙️) to configure your AI provider settings (OpenAI, Azure OpenAI, Anthropic, or Google AI).";
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
            // Determine if this is a new session (no previous AI responses)
            bool isNewSession = !session.Messages.Any(m => !m.IsUser);
            
            // Build conversation history for context
            var dynamicSystemPrompt = systemPrompt ?? BuildSystemPrompt(isNewSession);
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(ChatRole.System, dynamicSystemPrompt)
            };

            // Add conversation history (limit to last 10 messages for context window)
            var recentMessages = session.Messages.TakeLast(10).ToList();
            
            for (int i = 0; i < recentMessages.Count; i++)
            {
                var msg = recentMessages[i];
                
                // For the most recent user message, prepend the current editor code as context
                if (msg.IsUser && i == recentMessages.Count - 1 && !string.IsNullOrWhiteSpace(_currentEditorCode))
                {
                    var contentWithCode = $@"## Current Code in Editor:
```
{_currentEditorCode}
```

## User Request:
{msg.Content}";
                    messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, contentWithCode));
                }
                else
                {
                    messages.Add(new Microsoft.Extensions.AI.ChatMessage(
                        msg.IsUser ? ChatRole.User : ChatRole.Assistant,
                        msg.Content));
                }
            }

            // Some models only support temperature=1 or don't support temperature at all
            var modelName = _cachedSettings?.AIModel?.ToLowerInvariant() ?? "";
            var serviceType = _cachedSettings?.AIServiceType ?? "OpenAI";
            var isRestrictedModel = modelName.Contains("gpt-5") || 
                                    modelName.Contains("gpt5") || 
                                    modelName.StartsWith("o1") ||
                                    modelName.Contains("o1-preview") ||
                                    modelName.Contains("o1-mini");
            
            // Anthropic and Google handle temperature internally through their adapters
            var options = new ChatOptions
            {
                Temperature = isRestrictedModel ? null : (float?)temperature ?? 0.7f,
                MaxOutputTokens = maxTokens ?? 4096
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
