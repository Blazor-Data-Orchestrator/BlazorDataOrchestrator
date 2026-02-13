using System.Runtime.CompilerServices;
using Mscc.GenerativeAI;

// Alias to avoid ambiguity with Mscc.GenerativeAI.ChatMessage
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using AIChatResponse = Microsoft.Extensions.AI.ChatResponse;
using AIChatResponseUpdate = Microsoft.Extensions.AI.ChatResponseUpdate;
using AIChatOptions = Microsoft.Extensions.AI.ChatOptions;
using AIChatClientMetadata = Microsoft.Extensions.AI.ChatClientMetadata;
using Microsoft.Extensions.AI;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// IChatClient adapter for Google AI (Gemini) API.
/// Bridges the Microsoft.Extensions.AI abstraction with the Mscc.GenerativeAI SDK.
/// </summary>
public class GoogleAIChatClientAdapter : IChatClient
{
    private readonly string _apiKey;
    private readonly string _modelName;

    public GoogleAIChatClientAdapter(string apiKey, string model)
    {
        _apiKey = apiKey;
        _modelName = model;
    }

    public AIChatClientMetadata Metadata => new("GoogleAI", null, _modelName);

    public async Task<AIChatResponse> GetResponseAsync(
        IEnumerable<AIChatMessage> chatMessages,
        AIChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = CreateModel(options);
        var request = BuildRequest(chatMessages, options);

        var response = await model.GenerateContent(request);
        var text = response?.Text ?? "";

        return new AIChatResponse(new AIChatMessage(AIChatRole.Assistant, text));
    }

    public async IAsyncEnumerable<AIChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AIChatMessage> chatMessages,
        AIChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = CreateModel(options);
        var request = BuildRequest(chatMessages, options);

        await foreach (var response in model.GenerateContentStream(request))
        {
            var text = response?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return new AIChatResponseUpdate(AIChatRole.Assistant, text);
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(GoogleAIChatClientAdapter))
            return this;
        return null;
    }

    public void Dispose() { }

    private GenerativeModel CreateModel(AIChatOptions? options)
    {
        var googleAI = new GoogleAI(_apiKey);

        GenerationConfig? config = null;
        if (options != null)
        {
            config = new GenerationConfig();
            if (options.Temperature.HasValue)
                config.Temperature = options.Temperature.Value;
            if (options.MaxOutputTokens.HasValue)
                config.MaxOutputTokens = options.MaxOutputTokens.Value;
        }

        return googleAI.GenerativeModel(
            model: _modelName,
            generationConfig: config);
    }

    private static GenerateContentRequest BuildRequest(
        IEnumerable<AIChatMessage> chatMessages,
        AIChatOptions? options)
    {
        string? systemInstruction = null;
        var contents = new List<Content>();

        foreach (var msg in chatMessages)
        {
            if (msg.Role == AIChatRole.System)
            {
                systemInstruction = msg.Text;
                continue;
            }

            var role = msg.Role == AIChatRole.User ? Role.User : Role.Model;
            contents.Add(new Content(msg.Text ?? "") { Role = role });
        }

        var request = new GenerateContentRequest
        {
            Contents = contents
        };

        // Set system instruction if present
        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            request.SystemInstruction = new Content(systemInstruction) { Role = Role.User };
        }

        // Generation config is set on the model at creation time
        return request;
    }
}
