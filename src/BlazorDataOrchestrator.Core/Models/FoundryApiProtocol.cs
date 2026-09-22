namespace BlazorDataOrchestrator.Core.Models;

/// <summary>
/// The wire protocol used to talk to an Azure AI Foundry deployment.
/// </summary>
public enum FoundryApiProtocol
{
    /// <summary>Pick the protocol from the endpoint path, then the deployment name.</summary>
    Auto = 0,

    /// <summary>OpenAI Chat Completions: <c>{root}/openai/v1/chat/completions</c>.</summary>
    ChatCompletions = 1,

    /// <summary>OpenAI Responses: <c>{root}/openai/v1/responses</c>.</summary>
    Responses = 2,

    /// <summary>Anthropic Messages: <c>{root}/anthropic/v1/messages</c>.</summary>
    AnthropicMessages = 3
}
