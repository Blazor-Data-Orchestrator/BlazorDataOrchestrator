using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services.Foundry;

/// <summary>
/// The outcome of normalising an Azure AI Foundry endpoint.
/// </summary>
/// <param name="AccountRoot">Scheme and authority of the Foundry resource.</param>
/// <param name="Protocol">The resolved protocol. Never <see cref="FoundryApiProtocol.Auto"/>.</param>
/// <param name="BaseAddress">The base address the SDK client is constructed with.</param>
/// <param name="OperationUrl">Full request URL, shown in the UI and in error messages.</param>
/// <param name="Notices">Informational messages for the admin.</param>
public sealed record FoundryEndpointResolution(
    Uri AccountRoot,
    FoundryApiProtocol Protocol,
    Uri BaseAddress,
    string OperationUrl,
    IReadOnlyList<string> Notices);

/// <summary>
/// Normalises any Azure AI Foundry URL to a base address plus a wire protocol.
/// Pure function: no I/O, so the UI can call it on every keystroke.
/// </summary>
public static class FoundryEndpointResolver
{
    private static readonly string[] KnownHostSuffixes =
    {
        ".services.ai.azure.com",
        ".openai.azure.com",
        ".cognitiveservices.azure.com"
    };

    /// <summary>
    /// Resolves the endpoint, deployment, and requested protocol into a concrete request target.
    /// </summary>
    public static bool TryResolve(
        string? endpoint,
        string? deploymentName,
        FoundryApiProtocol requested,
        out FoundryEndpointResolution? resolution,
        out string? error)
    {
        resolution = null;
        error = null;

        var trimmed = (endpoint ?? "").Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            error = "Enter the Foundry endpoint, for example https://your-resource.services.ai.azure.com.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = $"'{trimmed}' is not a valid URL. Enter the full URL including https://.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "The endpoint must use https.";
            return false;
        }

        var notices = new List<string>();

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            notices.Add("The query string was removed. The Foundry v1 surfaces don't use an api-version parameter.");
        }

        var accountRoot = new Uri($"{uri.Scheme}://{uri.Authority}");
        var path = uri.AbsolutePath.TrimEnd('/');
        var isKnownHost = KnownHostSuffixes.Any(s => uri.Host.EndsWith(s, StringComparison.OrdinalIgnoreCase));

        var hint = DetectProtocolHint(path, notices);

        var protocol = requested;
        if (protocol == FoundryApiProtocol.Auto)
        {
            protocol = hint ?? InferProtocolFromModel(deploymentName);
        }
        else if (hint is not null && hint != protocol)
        {
            notices.Add($"The endpoint path suggests {Describe(hint.Value)}, but {Describe(protocol)} was selected. The selected protocol wins.");
        }

        if (!isKnownHost)
        {
            notices.Add($"'{uri.Host}' is not a recognised Azure AI host. The API key will be sent to this host as an OpenAI-compatible gateway.");

            // A gateway front door owns its own routing, so keep the path the admin entered.
            var gatewayBase = new Uri(path.Length == 0 ? accountRoot.ToString() : $"{accountRoot}{path.TrimStart('/')}");
            resolution = new FoundryEndpointResolution(
                accountRoot,
                protocol,
                gatewayBase,
                BuildOperationUrl(gatewayBase, protocol),
                notices);
            return true;
        }

        var baseAddress = protocol == FoundryApiProtocol.AnthropicMessages
            ? new Uri($"{accountRoot}anthropic")
            : new Uri($"{accountRoot}openai/v1");

        resolution = new FoundryEndpointResolution(
            accountRoot,
            protocol,
            baseAddress,
            BuildOperationUrl(baseAddress, protocol),
            notices);

        return true;
    }

    /// <summary>
    /// True when an Azure OpenAI configuration must use the Foundry v1 routing
    /// instead of the classic <c>openai/deployments/...</c> route.
    /// </summary>
    public static bool ShouldUseV1Routing(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var path = uri.AbsolutePath;

        return path.Contains("/openai/v1", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/anthropic", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase)
            || path.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Guesses the protocol from the deployment name. Only used when the URL gives no hint.
    /// </summary>
    public static FoundryApiProtocol InferProtocolFromModel(string? deploymentName)
    {
        var name = (deploymentName ?? "").Trim().ToLowerInvariant();

        if (name.Length == 0)
        {
            return FoundryApiProtocol.ChatCompletions;
        }

        if (name.StartsWith("claude"))
        {
            return FoundryApiProtocol.AnthropicMessages;
        }

        if (name.Contains("codex")
            || name.StartsWith("o1-pro")
            || name.StartsWith("o3-pro")
            || name.StartsWith("gpt-5-pro")
            || name.StartsWith("computer-use"))
        {
            return FoundryApiProtocol.Responses;
        }

        return FoundryApiProtocol.ChatCompletions;
    }

    /// <summary>Human readable protocol name used in notices and error messages.</summary>
    public static string Describe(FoundryApiProtocol protocol) => protocol switch
    {
        FoundryApiProtocol.Responses => "Responses",
        FoundryApiProtocol.AnthropicMessages => "Anthropic Messages",
        FoundryApiProtocol.ChatCompletions => "Chat Completions",
        _ => "Auto"
    };

    private static FoundryApiProtocol? DetectProtocolHint(string path, List<string> notices)
    {
        if (path.Contains("/anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return FoundryApiProtocol.AnthropicMessages;
        }

        if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
        {
            return FoundryApiProtocol.Responses;
        }

        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return FoundryApiProtocol.ChatCompletions;
        }

        if (path.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            notices.Add("A Foundry project URL was entered. It was rewritten to the resource endpoint, which is what chat requests use.");
        }

        return null;
    }

    private static string BuildOperationUrl(Uri baseAddress, FoundryApiProtocol protocol)
    {
        var root = baseAddress.ToString().TrimEnd('/');

        return protocol switch
        {
            FoundryApiProtocol.AnthropicMessages => $"{root}/v1/messages",
            FoundryApiProtocol.Responses => $"{root}/responses",
            _ => $"{root}/chat/completions"
        };
    }
}
