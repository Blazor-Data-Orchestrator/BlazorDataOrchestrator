using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using Azure.AI.OpenAI;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services.Foundry;

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
    private readonly IExternalToolProvider? _externalToolProvider;
    private readonly ILogger<CodeAssistantChatService>? _logger;
    private AISettings? _cachedSettings;
    private IChatClient? _chatClient;
    private FoundryEndpointResolution? _lastResolution;
    private string? _clientCreationError;
    private bool _suppressTemperature;
    private bool _suppressTools;
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
        IInstructionsProvider instructionsProvider,
        IExternalToolProvider? externalToolProvider = null,
        ILogger<CodeAssistantChatService>? logger = null)
    {
        _settingsService = settingsService;
        _instructionsProvider = instructionsProvider;
        _externalToolProvider = externalToolProvider;
        _logger = logger;
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
            _cachedSettings.ActiveServiceType == settings.ActiveServiceType &&
            _cachedSettings.Active.ApiKey == settings.Active.ApiKey &&
            _cachedSettings.Active.AIModel == settings.Active.AIModel &&
            _cachedSettings.Active.Endpoint == settings.Active.Endpoint &&
            _cachedSettings.Active.ApiVersion == settings.Active.ApiVersion &&
            _cachedSettings.Active.ApiProtocol == settings.Active.ApiProtocol &&
            _cachedSettings.Active.DeploymentPath == settings.Active.DeploymentPath)
        {
            return _chatClient;
        }

        _cachedSettings = settings;
        _clientCreationError = null;
        _lastResolution = null;
        _suppressTemperature = false;
        _suppressTools = false;

        if (!settings.IsConfigured)
        {
            _chatClient = null;
            return null;
        }

        try
        {
            _lastResolution = ResolveTargetOrNull(settings.Active);

            // Function invocation lets the model call MCP-backed tools without extra plumbing here.
            var innerClient = ChatClientFactory.Create(settings);
            _chatClient = innerClient is null
                ? null
                : innerClient.AsBuilder().UseFunctionInvocation().Build();

            if (_lastResolution is not null)
            {
                _logger?.LogInformation(
                    "AI chat client created. Target {OperationUrl}, protocol {Protocol}, deployment {Deployment}.",
                    _lastResolution.OperationUrl,
                    FoundryEndpointResolver.Describe(_lastResolution.Protocol),
                    settings.Active.AIModel);
            }
        }
        catch (AIConfigurationException ex)
        {
            _clientCreationError = ex.Message;
            _chatClient = null;
            _logger?.LogWarning("AI chat client configuration is invalid: {Reason}", ex.Message);
        }
        catch (Exception ex)
        {
            _clientCreationError = ex.Message;
            _chatClient = null;
            _logger?.LogWarning(ex, "AI chat client could not be created.");
        }

        return _chatClient;
    }

    /// <summary>
    /// Resolves the Foundry request target, or null for providers that don't use one.
    /// </summary>
    private static FoundryEndpointResolution? ResolveTargetOrNull(AIProviderSettings provider)
    {
        var usesFoundryRouting = provider.ServiceType == AIServiceType.AzureAIFoundry
            || (provider.ServiceType == AIServiceType.AzureOpenAI
                && FoundryEndpointResolver.ShouldUseV1Routing(provider.Endpoint));

        return usesFoundryRouting ? ChatClientFactory.ResolveFoundry(provider) : null;
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
            var fallbackResponse = _clientCreationError is null
                ? "⚠️ AI service is not configured. Please click the gear icon (⚙️) to configure your AI provider settings (OpenAI, Azure OpenAI, Azure AI Foundry, Anthropic, or Google AI)."
                : $"⚠️ AI configuration problem: {_clientCreationError}";
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

            // Some models reject a custom temperature or a tools array outright.
            var modelName = _cachedSettings?.Active.AIModel ?? "";
            var protocol = _lastResolution?.Protocol ?? FoundryApiProtocol.ChatCompletions;
            var allowTemperature = !_suppressTemperature && ModelCapabilities.SupportsTemperature(modelName, protocol);
            var allowTools = !_suppressTools && ModelCapabilities.SupportsTools(modelName, protocol);

            // Use at least 16384 output tokens so large code blocks are never truncated
            var effectiveMaxTokens = Math.Max(maxTokens ?? 16384, 8192);
            var options = new ChatOptions
            {
                Temperature = allowTemperature ? (float?)temperature ?? 0.7f : null,
                MaxOutputTokens = effectiveMaxTokens
            };

            if (allowTools && _externalToolProvider is not null)
            {
                try
                {
                    var tools = await _externalToolProvider.GetToolsAsync(includeWriteTools: false, cancellationToken);
                    if (tools.Count > 0)
                    {
                        options.Tools = tools;
                    }
                }
                catch (Exception)
                {
                    // A tool server outage must not break the chat.
                }
            }

            var responseBuilder = new System.Text.StringBuilder();
            
            await foreach (var update in StreamWithCapabilityRetryAsync(messages, options, cancellationToken))
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
            _logger?.LogWarning(ex, "AI request to {OperationUrl} failed with status {Status}.",
                _lastResolution?.OperationUrl ?? _cachedSettings?.Active.Endpoint ?? "(default)",
                AIErrorTranslator.ExtractStatus(ex));

            var errorMessage = AIErrorTranslator.Translate(ex, _lastResolution, _cachedSettings?.Active);
            session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = errorMessage });
            results.Clear();
            results.Add(errorMessage);
        }

        return results;
    }

    /// <summary>
    /// Streams the response, retrying once without temperature or tools when the
    /// deployment rejects them. The decision is remembered for the rest of the session.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithCapabilityRetryAsync(
        List<AIChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;

        try
        {
            enumerator = _chatClient!.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

            var started = false;

            while (true)
            {
                bool moved;

                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (!started && TryRelaxOptions(ex, options))
                {
                    await enumerator.DisposeAsync();
                    enumerator = _chatClient!.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    continue;
                }

                if (!moved)
                {
                    yield break;
                }

                started = true;
                yield return enumerator.Current;
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Removes the option the service rejected. Returns false when the error is unrelated
    /// or the option was already removed, so the caller surfaces the original failure.
    /// </summary>
    private bool TryRelaxOptions(Exception ex, ChatOptions options)
    {
        if (AIErrorTranslator.ExtractStatus(ex) != 400)
        {
            return false;
        }

        var message = ex.Message ?? "";

        if (options.Temperature is not null && message.Contains("temperature", StringComparison.OrdinalIgnoreCase))
        {
            options.Temperature = null;
            _suppressTemperature = true;
            _logger?.LogInformation("Retrying without temperature: the deployment rejected it.");
            return true;
        }

        if (options.Tools is { Count: > 0 } && message.Contains("tool", StringComparison.OrdinalIgnoreCase))
        {
            options.Tools = null;
            _suppressTools = true;
            _logger?.LogInformation("Retrying without tools: the deployment rejected them.");
            return true;
        }

        return false;
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
        _lastResolution = null;
        _clientCreationError = null;
        _suppressTemperature = false;
        _suppressTools = false;
    }
}
