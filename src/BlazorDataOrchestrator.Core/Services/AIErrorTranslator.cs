using System.ClientModel;
using System.Text.RegularExpressions;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services.Foundry;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Turns an AI service exception into an actionable admin message.
/// Never includes the API key.
/// </summary>
public static partial class AIErrorTranslator
{
    public const string Prefix = "❌ Error communicating with AI service:";

    public static string Translate(Exception ex, FoundryEndpointResolution? resolution, AIProviderSettings? settings)
    {
        if (ex is AIConfigurationException configError)
        {
            return $"{Prefix} {configError.Message}";
        }

        var status = ExtractStatus(ex);
        var body = ex.Message ?? "";
        var url = resolution?.OperationUrl ?? settings?.Endpoint ?? "the AI endpoint";
        var host = TryGetHost(url);
        var protocol = FoundryEndpointResolver.Describe(resolution?.Protocol ?? FoundryApiProtocol.ChatCompletions);
        var model = string.IsNullOrWhiteSpace(settings?.AIModel) ? "(none)" : settings!.AIModel;

        var detail = status switch
        {
            400 when Contains(body, "API version not supported") =>
                $"The endpoint {url} rejected the API version. If you pasted a Foundry project URL, use the resource URL https://<resource>.services.ai.azure.com, or select Azure AI Foundry as the service type.",

            400 when Contains(body, "operation does not work with the specified model")
                  || Contains(body, "unsupported_operation")
                  || Contains(body, "chat completions") =>
                $"Deployment '{model}' does not support {protocol}. Set API Protocol to Responses in the AI settings.",

            400 when Contains(body, "temperature") =>
                $"Deployment '{model}' does not accept a custom temperature. The request is retried automatically without it.",

            400 when Contains(body, "tools") =>
                $"Deployment '{model}' does not support tool calling. The request is retried automatically without tools.",

            401 =>
                $"The key was rejected by {host} for {protocol}. Check that the key belongs to this Foundry resource. Claude deployments need the Anthropic Messages protocol, and some models (for example Claude Mythos) accept Microsoft Entra ID only.",

            403 =>
                $"The key is valid but lacks access to deployment '{model}'. Check the deployment's access settings in the Foundry portal.",

            404 =>
                $"Deployment '{model}' was not found at {url}. Check the deployment name in Foundry portal > Deployments.",

            429 =>
                $"Rate limit or quota exceeded for '{model}'. Wait and retry, or raise the quota in the Foundry portal.",

            not null =>
                $"HTTP {status} from {url}: {Truncate(body)}",

            _ =>
                $"{Truncate(body)} (target: {url}, protocol: {protocol}, deployment: {model})"
        };

        return $"{Prefix} {detail}";
    }

    /// <summary>Extracts the HTTP status from the SDK-specific exception shapes.</summary>
    public static int? ExtractStatus(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case ClientResultException clientError:
                    return clientError.Status;
                case HttpRequestException { StatusCode: { } code }:
                    return (int)code;
            }

            var match = StatusPattern().Match(current.Message ?? "");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool Contains(string body, string needle)
        => body.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value)
    {
        value = (value ?? "").Trim();
        return value.Length <= 300 ? value : value[..300] + "…";
    }

    private static string TryGetHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    [GeneratedRegex(@"\b(4\d{2}|5\d{2})\b")]
    private static partial Regex StatusPattern();
}
