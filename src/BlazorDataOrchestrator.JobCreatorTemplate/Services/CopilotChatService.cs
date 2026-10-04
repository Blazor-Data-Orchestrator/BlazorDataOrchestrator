using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using Radzen.Blazor;
using BlazorDataOrchestrator.Core.Services;
using BlazorDataOrchestrator.Core.Services.AI;
using BlazorDataOrchestrator.Core.Models;
using CoreIAIChatService = BlazorDataOrchestrator.Core.Services.IAIChatService;
using ConversationSession = BlazorDataOrchestrator.Core.Models.ConversationSession;
using RadzenChatMessage = Radzen.Blazor.ChatMessage;
using GitHub.Copilot;

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
    private readonly IInstructionsProvider _instructionsProvider;
    private readonly CopilotHealthService _copilotHealth;

    // Editor state exposed to custom tools and the prompt. Null until the page provides it.
    private AIEditorContext? _editorContext;
    private string? _pendingCodeUpdate;

    // The job language each Copilot session was created with; a session's system prompt
    // (and therefore its skill) is fixed at creation, so a language change needs a new session.
    private readonly ConcurrentDictionary<string, string> _copilotSessionLanguages = new();

    private const string BaseSystemPrompt = @"You are a helpful code assistant specializing in Python and C# development. 
You help developers with:
- Writing and debugging code
- Explaining programming concepts
- Best practices and code optimization
- Understanding libraries and frameworks
Keep responses concise and focused on the code task at hand.

## Response Formatting Rules
- When providing code snippets or examples, ALWAYS wrap them in markdown fenced code blocks using triple backticks with the language identifier (e.g. ```csharp or ```python).
- When the response is a code update, you MUST surround the full PRIMARY code file with the markers ###UPDATED CODE BEGIN### and ###UPDATED CODE END### so the system can apply it.
- Place the fenced code block INSIDE the markers.
- NEVER return code outside of fenced code blocks.";

    public CopilotChatService(
        CopilotClient client,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<CopilotChatService> logger,
        IInstructionsProvider instructionsProvider,
        CopilotHealthService copilotHealth)
    {
        _client = client;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _instructionsProvider = instructionsProvider;
        _copilotHealth = copilotHealth;
    }

    private AIEditorContext EditorContext =>
        _editorContext ?? AIEditorContext.ForSingleFile(GetSelectedLanguage(), "");

    /// <summary>
    /// Sets the editor context (active file, primary code file, available files) included in AI requests.
    /// </summary>
    public void SetEditorContext(AIEditorContext context)
    {
        _editorContext = context;
    }

    /// <summary>
    /// Sets the current code from the editor to be included in AI requests.
    /// </summary>
    [Obsolete("Use SetEditorContext.")]
    public void SetCurrentEditorCode(string code)
    {
        _editorContext = AIEditorContext.ForSingleFile(EditorContext.Language, code);
    }

    /// <summary>
    /// Sets the current programming language context.
    /// </summary>
    [Obsolete("Use SetEditorContext.")]
    public void SetLanguage(string language)
    {
        _editorContext = AIEditorContext.ForSingleFile(language, EditorContext.PrimaryCodeContent);
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
    /// Builds the system prompt: base rules, file targeting rules, and the project SKILL.md
    /// (embedded in this assembly) for the current job language.
    /// </summary>
    internal string BuildSystemPrompt()
    {
        var language = EditorContext.Language;
        var info = _instructionsProvider.GetInfo(language);
        if (!info.Found || info.Length == 0)
        {
            _logger.LogWarning("No AI skill content for {Language}; the prompt contains base rules only.", language);
        }
        else
        {
            _logger.LogInformation("System prompt includes AI skill {Resource} ({Chars} chars, sha256 {Sha})",
                info.ResourceName, info.Length, info.Sha256);
        }

        return AIPromptComposer.Compose(BaseSystemPrompt, _instructionsProvider, language);
    }

    /// <summary>
    /// Creates the custom tools available to the Copilot session.
    /// </summary>
    private List<AIFunction> CreateTools()
    {
        var getEditorCode = AIFunctionFactory.Create(
            ([Description("No parameters needed")] string? _ = null) =>
            {
                var context = EditorContext;
                return new
                {
                    code = context.ActiveFileContent,
                    fileName = context.ActiveFileName,
                    language = context.Language,
                    primaryCodeFileName = context.PrimaryCodeFileName,
                    primaryCode = context.PrimaryCodeContent,
                    files = context.AvailableFiles
                };
            },
            "get_editor_code",
            "Returns the file open in the user's editor (which may be a .json settings file) and the PRIMARY code file (main.cs or main.py) where job code belongs");

        var getSelectedLanguage = AIFunctionFactory.Create(
            ([Description("No parameters needed")] string? _ = null) =>
            {
                return new { language = EditorContext.Language };
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
            ([Description("The complete updated content of the PRIMARY code file (main.cs or main.py)")] string code) =>
            {
                _pendingCodeUpdate = code;
                return new { success = true, appliedTo = EditorContext.PrimaryCodeFileName };
            },
            "apply_code_to_editor",
            "Replaces the PRIMARY code file (main.cs or main.py) with the complete updated code. Never use this for .json or other non-code files");

        return new List<AIFunction> { getEditorCode, getSelectedLanguage, getFileList, applyCode };
    }

    /// <summary>
    /// Gets or creates a CopilotSession for the given session ID. A session whose job language
    /// differs from the current one is replaced so the matching SKILL.md is in its system prompt.
    /// </summary>
    private async Task<CopilotSession> GetOrCreateCopilotSessionAsync(string sessionId, Func<string> systemPromptFactory)
    {
        var language = EditorContext.Language;
        if (_copilotSessions.TryGetValue(sessionId, out var existingSession))
        {
            if (!_copilotSessionLanguages.TryGetValue(sessionId, out var sessionLanguage) ||
                string.Equals(sessionLanguage, language, StringComparison.OrdinalIgnoreCase))
            {
                return existingSession;
            }

            _logger.LogInformation("Job language changed from {Old} to {New}; starting a new Copilot session", sessionLanguage, language);
            _copilotSessions.TryRemove(sessionId, out _);
            try
            {
                await existingSession.DisposeAsync();
                await _client.DeleteSessionAsync(sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error removing Copilot session {SessionId} after a language change", sessionId);
            }
        }
        else
        {
            try
            {
                // Try to resume an existing session first
                var session = await _client.ResumeSessionAsync(sessionId, new ResumeSessionConfig());
                _copilotSessions[sessionId] = session;
                _copilotSessionLanguages[sessionId] = language;
                return session;
            }
            catch
            {
                // Session doesn't exist, create a new one
            }
        }

        var model = _configuration.GetValue<string>("Copilot:Model") ?? "gpt-4.1";

        var newSession = await _client.CreateSessionAsync(new SessionConfig
        {
            SessionId = sessionId,
            Model = model,
            Streaming = true,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = systemPromptFactory()
            },
            Tools = CreateTools().Cast<AIFunctionDeclaration>().ToList(),
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = PermissionHandler.ApproveAll
        });

        _copilotSessions[sessionId] = newSession;
        _copilotSessionLanguages[sessionId] = language;
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
            // Early-exit if Copilot is not ready (CLI missing / not authenticated)
            if (!_copilotHealth.IsReady)
            {
                var guidance = $"⚠️ **Copilot is not available:** {_copilotHealth.StatusMessage}\n\n{CopilotHealthService.InstallInstructions}";
                _logger.LogWarning("Chat request skipped — CopilotHealthService.IsReady is false: {Status}", _copilotHealth.StatusMessage);
                session.Messages.Add(new RadzenChatMessage { IsUser = false, Content = guidance });
                writer.TryWrite(guidance);
                writer.TryComplete();
                return;
            }

            // Build the prompt with the labeled editor context (active file vs. primary code file)
            var context = EditorContext;
            var fullPrompt = string.IsNullOrWhiteSpace(context.ActiveFileContent) && string.IsNullOrWhiteSpace(context.PrimaryCodeContent)
                ? userInput
                : EditorContextFormatter.Format(context, userInput);

            // Ensure the client is started
            bool clientConnected;
            try { await _client.GetStatusAsync(); clientConnected = true; }
            catch { clientConnected = false; }

            if (!clientConnected)
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

            // The system prompt (with the SKILL.md) is only used when a Copilot session is created
            Func<string> systemPromptFactory = () => systemPrompt ?? BuildSystemPrompt();

            CopilotSession copilotSession;
            try
            {
                copilotSession = await GetOrCreateCopilotSessionAsync(
                    sessionId ?? session.Id,
                    systemPromptFactory);
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
            using var subscription = copilotSession.On<SessionEvent>(evt =>
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
                _logger.LogWarning("Copilot request timed out after 120s. Client runtime port: {Port}. This may indicate the Copilot CLI is not authenticated.", _client.RuntimePort);
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
        _copilotSessionLanguages.TryRemove(sessionId, out _);
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
            _copilotSessionLanguages.TryRemove(sid, out _);
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
        _copilotSessionLanguages.Clear();
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
