namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Redirects Anthropic.SDK requests from api.anthropic.com to an Azure AI Foundry
/// Anthropic passthrough base. The SDK's <c>x-api-key</c> and <c>anthropic-version</c>
/// headers are exactly what Foundry expects, so they are left untouched.
/// </summary>
internal sealed class AnthropicBaseAddressHandler : DelegatingHandler
{
    private static readonly Uri AnthropicBase = new("https://api.anthropic.com/");

    private readonly string _foundryBase;

    public AnthropicBaseAddressHandler(Uri foundryBase, HttpMessageHandler innerHandler)
    {
        _foundryBase = foundryBase.ToString().TrimEnd('/');
        InnerHandler = innerHandler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && AnthropicBase.IsBaseOf(uri))
        {
            var relative = AnthropicBase.MakeRelativeUri(uri).ToString();
            request.RequestUri = new Uri($"{_foundryBase}/{relative}");
        }

        return base.SendAsync(request, cancellationToken);
    }

    // The inner handler is shared across adapters, so it must outlive this instance.
    protected override void Dispose(bool disposing) { }
}
