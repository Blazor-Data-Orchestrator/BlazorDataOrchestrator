using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using Azure.AI.OpenAI;
using Radzen;
using Radzen.Blazor;
using Microsoft.AspNetCore.Hosting;
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
    private readonly IWebHostEnvironment _environment;
    private AISettings? _cachedSettings;
    private IChatClient? _chatClient;
    private string? _cachedInstructions;
    private string? _cachedInstructionsLanguage;
    
    // Property to hold the current code from the editor
    private string _currentEditorCode = "";
    
    private const string BaseSystemPrompt = @"You are a helpful code assistant specializing in Python and C# development. 
You help developers with:
- Writing and debugging code
- Explaining programming concepts
- Best practices and code optimization
- Understanding libraries and frameworks
Keep responses concise and focused on the code task at hand.";

    public CodeAssistantChatService(AISettingsService settingsService, IWebHostEnvironment environment)
    {
        _settingsService = settingsService;
        _environment = environment;
    }
    
    /// <summary>
    /// Sets the current code from the editor to be included in AI requests.
    /// </summary>
    public void SetCurrentEditorCode(string code)
    {
        _currentEditorCode = code ?? "";
    }
    
    /// <summary>
    /// Gets the selected language from the configuration file.
    /// </summary>
    private string GetSelectedLanguage()
    {
        try
        {
            var configPath = Path.Combine(_environment.ContentRootPath, "Code", "configuration.json");
            if (File.Exists(configPath))
            {
                var configJson = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(configJson);
                if (doc.RootElement.TryGetProperty("SelectedLanguage", out var langElement))
                {
                    return langElement.GetString()?.ToLowerInvariant() ?? "csharp";
                }
            }
        }
        catch
        {
            // Fall back to default
        }
        return "csharp";
    }
    
    /// <summary>
    /// Loads the custom instructions for the selected language.
    /// </summary>
    private async Task<string> GetLanguageInstructionsAsync()
    {
        var selectedLanguage = GetSelectedLanguage();
        
        // Return cached instructions if already loaded for this language
        if (_cachedInstructions != null && _cachedInstructionsLanguage == selectedLanguage)
        {
            return _cachedInstructions;
        }
        
        string instructionsFile = selectedLanguage switch
        {
            "python" => Path.Combine(_environment.ContentRootPath, "..", "..", ".github", "skills", "python.instructions.md"),
            _ => Path.Combine(_environment.ContentRootPath, "..", "..", ".github", "skills", "csharp.instructions.md")
        };
        
        try
        {
            if (File.Exists(instructionsFile))
            {
                _cachedInstructions = await File.ReadAllTextAsync(instructionsFile);
                _cachedInstructionsLanguage = selectedLanguage;
                return _cachedInstructions;
            }
        }
        catch
        {
            // Fall back to empty instructions
        }
        
        _cachedInstructions = "";
        _cachedInstructionsLanguage = selectedLanguage;
        return _cachedInstructions;
    }
    
    /// <summary>
    /// Builds the dynamic system prompt with language-specific instructions.
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(bool isNewSession)
    {
        var promptBuilder = new System.Text.StringBuilder();
        promptBuilder.AppendLine(BaseSystemPrompt);
        
        // Add language-specific instructions for new sessions
        if (isNewSession)
        {
            var instructions = await GetLanguageInstructionsAsync();
            if (!string.IsNullOrWhiteSpace(instructions))
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("## Custom Instructions for Code Generation");
                promptBuilder.AppendLine(instructions);
            }
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
            // Determine if this is a new session (no previous AI responses)
            bool isNewSession = !session.Messages.Any(m => !m.IsUser);
            
            // Build conversation history for context
            var dynamicSystemPrompt = systemPrompt ?? await BuildSystemPromptAsync(isNewSession);
            var messages = new List<AIChatMessage>
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
                    messages.Add(new AIChatMessage(ChatRole.User, contentWithCode));
                }
                else
                {
                    messages.Add(new AIChatMessage(
                        msg.IsUser ? ChatRole.User : ChatRole.Assistant,
                        msg.Content));
                }
            }

            // GPT-5 and o1 models only support temperature=1, so don't set it for those models
            var modelName = _cachedSettings?.AIModel?.ToLowerInvariant() ?? "";
            var isRestrictedModel = modelName.Contains("gpt-5") || 
                                    modelName.Contains("gpt5") || 
                                    modelName.StartsWith("o1") ||
                                    modelName.Contains("o1-preview") ||
                                    modelName.Contains("o1-mini");
            
            var options = new ChatOptions
            {
                Temperature = isRestrictedModel ? null : (float?)temperature ?? 0.7f,
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
