using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services.Foundry;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class FoundryEndpointResolverTests
{
    // Cases U1-U10 from the Azure AI Foundry endpoint fix plan.
    public static TheoryData<string, string, FoundryApiProtocol, FoundryApiProtocol, string> ResolvedCases => new()
    {
        { "https://r.services.ai.azure.com/openai/v1/responses", "gpt-5-codex", FoundryApiProtocol.Auto, FoundryApiProtocol.Responses, "https://r.services.ai.azure.com/openai/v1" },
        { "https://r.services.ai.azure.com/anthropic/v1/messages", "claude-sonnet-4-6", FoundryApiProtocol.Auto, FoundryApiProtocol.AnthropicMessages, "https://r.services.ai.azure.com/anthropic" },
        { "https://r.services.ai.azure.com/api/projects/p1", "gpt-4.1", FoundryApiProtocol.Auto, FoundryApiProtocol.ChatCompletions, "https://r.services.ai.azure.com/openai/v1" },
        { "https://r.services.ai.azure.com/", "claude-opus-5-5", FoundryApiProtocol.Auto, FoundryApiProtocol.AnthropicMessages, "https://r.services.ai.azure.com/anthropic" },
        { "https://r.services.ai.azure.com/", "my-claude", FoundryApiProtocol.AnthropicMessages, FoundryApiProtocol.AnthropicMessages, "https://r.services.ai.azure.com/anthropic" },
        { "https://r.services.ai.azure.com/openai/v1/responses", "gpt-4o", FoundryApiProtocol.ChatCompletions, FoundryApiProtocol.ChatCompletions, "https://r.services.ai.azure.com/openai/v1" },
        { "https://r.openai.azure.com/openai/v1?api-version=2024-10-21", "gpt-4o", FoundryApiProtocol.Auto, FoundryApiProtocol.ChatCompletions, "https://r.openai.azure.com/openai/v1" },
        { "https://r.services.ai.azure.com/", "Llama-4-Maverick", FoundryApiProtocol.Auto, FoundryApiProtocol.ChatCompletions, "https://r.services.ai.azure.com/openai/v1" }
    };

    [Theory]
    [MemberData(nameof(ResolvedCases))]
    public void TryResolve_ProducesExpectedProtocolAndBase(
        string endpoint,
        string deployment,
        FoundryApiProtocol requested,
        FoundryApiProtocol expectedProtocol,
        string expectedBase)
    {
        var resolved = FoundryEndpointResolver.TryResolve(endpoint, deployment, requested, out var resolution, out var error);

        Assert.True(resolved, error);
        Assert.Equal(expectedProtocol, resolution!.Protocol);
        Assert.Equal(expectedBase, resolution.BaseAddress.ToString().TrimEnd('/'));
    }

    [Fact]
    public void TryResolve_ProjectUrl_AddsRewriteNotice()
    {
        FoundryEndpointResolver.TryResolve(
            "https://r.services.ai.azure.com/api/projects/p1", "gpt-4.1", FoundryApiProtocol.Auto, out var resolution, out _);

        Assert.Contains(resolution!.Notices, n => n.Contains("project URL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryResolve_ApiVersionQuery_AddsDroppedNotice()
    {
        FoundryEndpointResolver.TryResolve(
            "https://r.openai.azure.com/openai/v1?api-version=2024-10-21", "gpt-4o", FoundryApiProtocol.Auto, out var resolution, out _);

        Assert.Contains(resolution!.Notices, n => n.Contains("query string", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryResolve_ExplicitProtocolConflictingWithPath_AddsNotice()
    {
        FoundryEndpointResolver.TryResolve(
            "https://r.services.ai.azure.com/openai/v1/responses", "gpt-4o", FoundryApiProtocol.ChatCompletions, out var resolution, out _);

        Assert.Contains(resolution!.Notices, n => n.Contains("Responses", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://r.services.ai.azure.com", "https")]
    [InlineData("not a url", "valid URL")]
    [InlineData("", "Enter the Foundry endpoint")]
    public void TryResolve_InvalidEndpoint_ReturnsActionableError(string endpoint, string expectedFragment)
    {
        var resolved = FoundryEndpointResolver.TryResolve(endpoint, "gpt-4o", FoundryApiProtocol.Auto, out var resolution, out var error);

        Assert.False(resolved);
        Assert.Null(resolution);
        Assert.Contains(expectedFragment, error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://r.services.ai.azure.com/openai/v1/responses", FoundryApiProtocol.Responses, "https://r.services.ai.azure.com/openai/v1/responses")]
    [InlineData("https://r.services.ai.azure.com/anthropic/v1/messages", FoundryApiProtocol.AnthropicMessages, "https://r.services.ai.azure.com/anthropic/v1/messages")]
    [InlineData("https://r.services.ai.azure.com", FoundryApiProtocol.ChatCompletions, "https://r.services.ai.azure.com/openai/v1/chat/completions")]
    public void TryResolve_BuildsOperationUrlForProtocol(string endpoint, FoundryApiProtocol requested, string expectedUrl)
    {
        FoundryEndpointResolver.TryResolve(endpoint, "gpt-4o", requested, out var resolution, out _);

        Assert.Equal(expectedUrl, resolution!.OperationUrl);
    }

    [Theory]
    [InlineData("claude-sonnet-4-6", FoundryApiProtocol.AnthropicMessages)]
    [InlineData("gpt-5-codex", FoundryApiProtocol.Responses)]
    [InlineData("codex-mini", FoundryApiProtocol.Responses)]
    [InlineData("o3-pro", FoundryApiProtocol.Responses)]
    [InlineData("gpt-4o", FoundryApiProtocol.ChatCompletions)]
    [InlineData("gpt-5", FoundryApiProtocol.ChatCompletions)]
    [InlineData("Llama-4-Maverick", FoundryApiProtocol.ChatCompletions)]
    [InlineData("DeepSeek-R1", FoundryApiProtocol.ChatCompletions)]
    [InlineData("", FoundryApiProtocol.ChatCompletions)]
    public void InferProtocolFromModel_MapsModelFamilies(string deployment, FoundryApiProtocol expected)
        => Assert.Equal(expected, FoundryEndpointResolver.InferProtocolFromModel(deployment));

    [Theory]
    [InlineData("https://r.services.ai.azure.com", true)]
    [InlineData("https://r.openai.azure.com/openai/v1", true)]
    [InlineData("https://r.openai.azure.com/anthropic", true)]
    [InlineData("https://r.services.ai.azure.com/api/projects/p1", true)]
    [InlineData("https://r.openai.azure.com", false)]
    [InlineData("https://r.openai.azure.com/", false)]
    [InlineData("", false)]
    public void ShouldUseV1Routing_DetectsFoundrySurfaces(string endpoint, bool expected)
        => Assert.Equal(expected, FoundryEndpointResolver.ShouldUseV1Routing(endpoint));
}
