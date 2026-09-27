using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;
using BlazorDataOrchestrator.Core.Services.Foundry;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class ModelCapabilitiesTests
{
    [Theory]
    [InlineData("gpt-4o", FoundryApiProtocol.ChatCompletions, true)]
    [InlineData("gpt-5", FoundryApiProtocol.ChatCompletions, false)]
    [InlineData("o1-mini", FoundryApiProtocol.ChatCompletions, false)]
    [InlineData("o3-pro", FoundryApiProtocol.Responses, false)]
    [InlineData("o4-mini", FoundryApiProtocol.ChatCompletions, false)]
    [InlineData("gpt-5-codex", FoundryApiProtocol.Responses, false)]
    [InlineData("claude-opus-5-5", FoundryApiProtocol.AnthropicMessages, false)]
    [InlineData("Llama-4-Maverick", FoundryApiProtocol.ChatCompletions, true)]
    public void SupportsTemperature_MatchesModelRules(string model, FoundryApiProtocol protocol, bool expected)
        => Assert.Equal(expected, ModelCapabilities.SupportsTemperature(model, protocol));

    [Theory]
    [InlineData("gpt-4o", FoundryApiProtocol.ChatCompletions, true)]
    [InlineData("claude-opus-5-5", FoundryApiProtocol.AnthropicMessages, false)]
    [InlineData("DeepSeek-R1", FoundryApiProtocol.ChatCompletions, false)]
    public void SupportsTools_MatchesModelRules(string model, FoundryApiProtocol protocol, bool expected)
        => Assert.Equal(expected, ModelCapabilities.SupportsTools(model, protocol));
}

public class AIErrorTranslatorTests
{
    private static readonly AIProviderSettings Settings = new()
    {
        ServiceType = AIServiceType.AzureAIFoundry,
        ApiKey = "super-secret-key",
        AIModel = "gpt-4.1",
        Endpoint = "https://r.services.ai.azure.com"
    };

    private static FoundryEndpointResolution Resolution()
    {
        FoundryEndpointResolver.TryResolve(Settings.Endpoint, Settings.AIModel, FoundryApiProtocol.Auto, out var resolution, out _);
        return resolution!;
    }

    [Fact]
    public void Translate_ConfigurationError_ReturnsMessageVerbatim()
    {
        var message = AIErrorTranslator.Translate(new AIConfigurationException("The endpoint must use https."), null, Settings);

        Assert.Contains("The endpoint must use https.", message);
    }

    [Fact]
    public void Translate_ApiVersionRejected_SuggestsResourceUrl()
    {
        var ex = new ClientResultException("API version not supported", Fake(400));

        var message = AIErrorTranslator.Translate(ex, Resolution(), Settings);

        Assert.Contains("services.ai.azure.com", message);
        Assert.Contains("project URL", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Translate_Unauthorized_MentionsProtocolAndNeverTheKey()
    {
        var ex = new ClientResultException("Access denied", Fake(401));

        var message = AIErrorTranslator.Translate(ex, Resolution(), Settings);

        Assert.Contains("Chat Completions", message);
        Assert.DoesNotContain(Settings.ApiKey, message);
    }

    [Fact]
    public void Translate_NotFound_NamesDeploymentAndUrl()
    {
        var ex = new ClientResultException("Resource not found", Fake(404));

        var message = AIErrorTranslator.Translate(ex, Resolution(), Settings);

        Assert.Contains("gpt-4.1", message);
        Assert.Contains("/openai/v1/chat/completions", message);
    }

    [Fact]
    public void ExtractStatus_ReadsStatusFromHttpRequestException()
    {
        var ex = new HttpRequestException("boom", null, HttpStatusCode.TooManyRequests);

        Assert.Equal(429, AIErrorTranslator.ExtractStatus(ex));
    }

    private static PipelineResponse Fake(int status) => new FakeResponse(status);

    private sealed class FakeResponse(int status) : PipelineResponse
    {
        public override int Status { get; } = status;
        public override string ReasonPhrase => "";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.FromString("");
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);
        public override void Dispose() { }
    }
}
