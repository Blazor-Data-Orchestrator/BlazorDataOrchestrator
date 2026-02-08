using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using Radzen.Blazor;
using BlazorDataOrchestrator.Core.Services;
using BlazorDataOrchestrator.Core.Models;
using GitHub.Copilot.SDK;
using CoreIAIChatService = BlazorDataOrchestrator.Core.Services.IAIChatService;
using ConversationSession = BlazorDataOrchestrator.Core.Models.ConversationSession;
using RadzenChatMessage = Radzen.Blazor.ChatMessage;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services;

/// <summary>
/// AI Chat service for code assistance using the GitHub Copilot SDK.
/// Replaces the OpenAI/Azure OpenAI based CodeAssistantChatService.
/// </summary>
public class CopilotChatService : CoreIAIChatService, Radzen.IAIChatService
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    private readonly ConcurrentDictionary<string, CopilotSession> _copilotSessions = new();
    private readonly CopilotClient _client;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CopilotChatService> _logger;

    // Editor state exposed to custom tools
    private string _currentEditorCode = "";
    private string _currentLanguage = "csharp";
    private string _currentFileName = "Program.cs";
    private string? _pendingCodeUpdate;
    private string? _cachedInstructions;
    private string? _cachedInstructionsLanguage;

    private const string BaseSystemPrompt = @"You are a helpful code assistant specializing in Python and C# development. 
You help developers with:
- Writing and debugging code
- Explaining programming concepts
- Best practices and code optimization
- Understanding libraries and frameworks
Keep responses concise and focused on the code task at hand.";

    public CopilotChatService(
        CopilotClient client,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<CopilotChatService> logger)
    {
        _client = client;
        _configuration = configuration;
        _environment = environment;
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
        if (_cachedInstructionsLanguage != language)
        {
            _cachedInstructions = null;
            _cachedInstructionsLanguage = language;
        }
        _currentLanguage = language ?? "csharp";
    }

    /// <summary>
    /// Sets the current file name for tool context.
    /// </summary>
    public void SetCurrentFileName(string fileName)
    {
        _currentFileName = fileName ?? "Program.cs";
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
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(BaseSystemPrompt);

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

    /// <summary>
    /// Creates the custom tools available to the Copilot session.
    /// </summary>
    private List<AIFunction> CreateTools()
    {
        var getEditorCode = AIFunctionFactory.Create(
            ([Description("No parameters needed")] string? _ = null) =>
            {
                return new
                {
                    code = _currentEditorCode,
                    fileName = _currentFileName,
                    language = _currentLanguage
                };
            },
            "get_editor_code",
            "Returns the current code open in the user's editor");

        var getSelectedLanguage = AIFunctionFactory.Create(
            ([Description("No parameters needed")] string? _ = null) =>
            {
                return new { language = _currentLanguage };
            },
            "get_selected_language",
            "Returns the currently selected programming language");

        var getFileList = AIFunctionFactory.Create(
            ([Description("No parameters needed")] string? _ = null) =>
            {
                try
                {
                    var codePath = Path.Combine(_environment.ContentRootPath, "Code");
                    if (Directory.Exists(codePath))
                    {
                        return Directory.GetFiles(codePath, "*.*", SearchOption.AllDirectories)
                            .Select(f => Path.GetRelativePath(codePath, f))
                            .ToArray();
                    }
                }
                catch
                {
                    // ignore
                }
                return Array.Empty<string>();
            },
            "get_file_list",
            "Lists available files in the current code folder");

        var applyCode = AIFunctionFactory.Create(
            ([Description("The complete updated code to apply")] string code) =>
            {
                _pendingCodeUpdate = code;
                return new { success = true };
            },
            "apply_code_to_editor",
            "Applies updated code to the editor, replacing the current content");

        return new List<AIFunction> { getEditorCode, getSelectedLanguage, getFileList, applyCode };
    }

    /// <summary>
    /// Gets or creates a CopilotSession for the given session ID.
    /// </summary>
    private async Task<CopilotSession> GetOrCreateCopilotSessionAsync(string sessionId, string systemPrompt)
    {
        if (_copilotSessions.TryGetValue(sessionId, out var existingSession))
        {
            return existingSession;
        }

        var model = _configuration.GetValue<string>("Copilot:Model") ?? "gpt-4.1";

        try
        {
            // Try to resume an existing session first
            var session = await _client.ResumeSessionAsync(sessionId);
            _copilotSessions[sessionId] = session;
            return session;
        }
        catch
        {
            // Session doesn't exist, create a new one
        }

        var newSession = await _client.CreateSessionAsync(new SessionConfig
        {
            SessionId = sessionId,
            Model = model,
            Streaming = true,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = systemPrompt
            },
            Tools = CreateTools(),
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false }
        });

        _copilotSessions[sessionId] = newSession;
        return newSession;
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

        // Use a Channel to bridge the async processing (which needs try/catch) to IAsyncEnumerable
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Start the processing in a background task so we can yield from the channel reader
        _ = ProcessCopilotRequestAsync(session, userInput, sessionId, systemPrompt, cancellationToken, channel.Writer);

        // Yield results from the channel
        await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Processes the Copilot request in a background task, writing results to the channel.
    /// This allows error handling with try/catch while the caller yields from the channel.
    /// </summary>
    private async Task ProcessCopilotRequestAsync(
        ConversationSession session,
        string userInput,
        string? sessionId,
        string? systemPrompt,
        CancellationToken cancellationToken,
        ChannelWriter<string> writer)
    {
        try
        {
            // Build the prompt with editor context
            var promptBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_currentEditorCode))
            {
                promptBuilder.AppendLine("## Current Code in Editor:");
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine(_currentEditorCode);
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("## User Request:");
            }
            promptBuilder.AppendLine(userInput);
            var fullPrompt = promptBuilder.ToString();

            // Ensure the client is started
            if (_client.State != ConnectionState.Connected)
            {
                try
                {
                    await _client.StartAsync();
                }
                catch (Exception ex)
                {
                    var errorMsg = $"⚠️ Failed to start Copilot client: {ex.Message}\n\nPlease ensure the GitHub Copilot CLI is installed and authenticated. Run `copilot --version` to verify.";
                    _logger.LogError(ex, "Failed to start CopilotClient");
                    session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = errorMsg });
                    writer.TryWrite(errorMsg);
                    writer.TryComplete();
                    return;
                }
            }

            // Build the system prompt
            bool isNewSession = !session.Messages.Any(m => !m.IsUser);
            var dynamicSystemPrompt = systemPrompt ?? await BuildSystemPromptAsync(isNewSession);

            CopilotSession copilotSession;
            try
            {
                copilotSession = await GetOrCreateCopilotSessionAsync(
                    sessionId ?? session.Id,
                    dynamicSystemPrompt);
            }
            catch (Exception ex)
            {
                var errorMsg = $"⚠️ Failed to create Copilot session: {ex.Message}";
                _logger.LogError(ex, "Failed to create CopilotSession");
                session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = errorMsg });
                writer.TryWrite(errorMsg);
                writer.TryComplete();
                return;
            }

            var responseBuilder = new StringBuilder();
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Subscribe to session events
            using var subscription = copilotSession.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        var chunk = delta.Data.DeltaContent;
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            responseBuilder.Append(chunk);
                            writer.TryWrite(chunk);
                        }
                        break;

                    case AssistantMessageEvent:
                        // Final complete message - don't re-emit if we already streamed deltas
                        break;

                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;

                    case SessionErrorEvent err:
                        _logger.LogError("Copilot session error: {Message}", err.Data.Message);
                        done.TrySetException(new Exception($"Copilot session error: {err.Data.Message}"));
                        break;
                }
            });

            // Send the message
            await copilotSession.SendAsync(new MessageOptions { Prompt = fullPrompt });

            // Wait for the done signal with a timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(120));

            try
            {
                await done.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try { await copilotSession.AbortAsync(); } catch { }
                var cancelMsg = "⚠️ Request was cancelled.";
                session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = cancelMsg });
                writer.TryWrite(cancelMsg);
                return;
            }
            catch (OperationCanceledException)
            {
                // Timeout
                try { await copilotSession.AbortAsync(); } catch { }
                var timeoutMsg = "⚠️ Request timed out. This is often caused by the Copilot CLI not being authenticated.\n\n" +
                    "To authenticate, use one of these methods:\n" +
                    "- Set the `GITHUB_TOKEN` environment variable\n" +
                    "- Run `copilot login` from the CLI binary in the app's runtimes folder\n" +
                    "- Run `gh auth login` if GitHub CLI is installed";
                _logger.LogWarning("Copilot request timed out after 120s. Client state: {State}. This may indicate the Copilot CLI is not authenticated.", _client.State);
                session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = timeoutMsg });
                writer.TryWrite(timeoutMsg);
                return;
            }

            // Store the complete response in the session
            var fullResponse = responseBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(fullResponse))
            {
                session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = fullResponse });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Copilot chat completion");
            var errorMsg = $"❌ Error communicating with Copilot: {ex.Message}";
            session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = errorMsg });
            writer.TryWrite(errorMsg);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Gets any pending code update from a tool invocation.
    /// </summary>
    public string? ConsumePendingCodeUpdate()
    {
        var update = _pendingCodeUpdate;
        _pendingCodeUpdate = null;
        return update;
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

        // Also clean up the Copilot session
        if (_copilotSessions.TryRemove(sessionId, out var copilotSession))
        {
            try
            {
                copilotSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _client.DeleteSessionAsync(sessionId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up Copilot session {SessionId}", sessionId);
            }
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

        foreach (var sid in oldSessions)
        {
            _sessions.TryRemove(sid, out _);
            if (_copilotSessions.TryRemove(sid, out var copilotSession))
            {
                try
                {
                    copilotSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Refreshes the client - clears cached sessions so new ones will be created with updated settings.
    /// </summary>
    public void RefreshClient()
    {
        // Dispose all Copilot sessions so they're re-created with new config
        foreach (var kvp in _copilotSessions)
        {
            try
            {
                kvp.Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { }
        }
        _copilotSessions.Clear();
    }

    // Explicit interface implementations for Radzen.IAIChatService
    // These bridge the Radzen types to our Core types

    Radzen.ConversationSession Radzen.IAIChatService.GetOrCreateSession(string? sessionId)
    {
        var coreSession = GetOrCreateSession(sessionId);
        return new Radzen.ConversationSession
        {
            Id = coreSession.Id,
            CreatedAt = coreSession.CreatedAt,
            Messages = coreSession.Messages
        };
    }

    IEnumerable<Radzen.ConversationSession> Radzen.IAIChatService.GetActiveSessions()
    {
        return _sessions.Values.Select(s => new Radzen.ConversationSession
        {
            Id = s.Id,
            CreatedAt = s.CreatedAt,
            Messages = s.Messages
        }).ToList();
    }
}
