using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

// Alias to resolve ambiguity between Microsoft.Extensions.AI.TextContent and Anthropic.SDK.Messaging.TextContent
using AnthropicTextContent = Anthropic.SDK.Messaging.TextContent;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// IChatClient adapter for Anthropic Claude API.
/// Bridges the Microsoft.Extensions.AI abstraction with the Anthropic.SDK v4.0.0.
/// </summary>
public class AnthropicChatClientAdapter : IChatClient
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicChatClientAdapter(string apiKey, string model)
    {
        _client = new AnthropicClient(apiKey);
        _model = model;
    }

    public ChatClientMetadata Metadata => new("Anthropic", null, _model);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (systemPrompt, messages) = ConvertMessages(chatMessages);

        var parameters = new MessageParameters
        {
            Model = _model,
            MaxTokens = options?.MaxOutputTokens ?? 2048,
            Messages = messages,
            Stream = false
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            parameters.SystemMessage = systemPrompt;
        }

        if (options?.Temperature.HasValue == true)
        {
            parameters.Temperature = (decimal)options.Temperature.Value;
        }

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

        var responseText = string.Join("", response.Content
            .OfType<AnthropicTextContent>()
            .Select(c => c.Text ?? ""));

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (systemPrompt, messages) = ConvertMessages(chatMessages);

        var parameters = new MessageParameters
        {
            Model = _model,
            MaxTokens = options?.MaxOutputTokens ?? 2048,
            Messages = messages,
            Stream = true
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            parameters.SystemMessage = systemPrompt;
        }

        if (options?.Temperature.HasValue == true)
        {
            parameters.Temperature = (decimal)options.Temperature.Value;
        }

        await foreach (var messageResponse in _client.Messages.StreamClaudeMessageAsync(parameters, cancellationToken))
        {
            if (messageResponse.Delta?.Text != null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, messageResponse.Delta.Text);
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(AnthropicChatClientAdapter))
            return this;
        return null;
    }

    public void Dispose() { }

    private static (string? SystemPrompt, List<Anthropic.SDK.Messaging.Message> Messages) ConvertMessages(
        IEnumerable<ChatMessage> chatMessages)
    {
        string? systemPrompt = null;
        var messages = new List<Anthropic.SDK.Messaging.Message>();

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                systemPrompt = msg.Text;
                continue;
            }

            var role = msg.Role == ChatRole.User
                ? RoleType.User
                : RoleType.Assistant;

            messages.Add(new Anthropic.SDK.Messaging.Message(role, msg.Text ?? ""));
        }

        // Anthropic requires messages to start with a user message
        // and alternate between user/assistant. Merge consecutive same-role messages.
        var mergedMessages = new List<Anthropic.SDK.Messaging.Message>();
        foreach (var msg in messages)
        {
            if (mergedMessages.Count > 0 && mergedMessages[^1].Role == msg.Role)
            {
                // Merge with previous message of same role
                var prev = mergedMessages[^1];
                var prevText = prev.Content?.OfType<AnthropicTextContent>().FirstOrDefault()?.Text ?? "";
                var currentText = msg.Content?.OfType<AnthropicTextContent>().FirstOrDefault()?.Text ?? "";
                mergedMessages[^1] = new Anthropic.SDK.Messaging.Message(
                    prev.Role,
                    prevText + "\n" + currentText);
            }
            else
            {
                mergedMessages.Add(msg);
            }
        }

        return (systemPrompt, mergedMessages);
    }
}
