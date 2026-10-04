using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services.AI;

/// <summary>Maps job file names to their kind and markdown fence language.</summary>
public static class JobFileTypes
{
    public static JobFileKind GetKind(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).ToLowerInvariant();
        if (name == "requirements.txt") return JobFileKind.Requirements;

        return Path.GetExtension(name) switch
        {
            ".cs" => JobFileKind.CSharp,
            ".py" => JobFileKind.Python,
            ".json" => JobFileKind.Json,
            ".nuspec" => JobFileKind.Nuspec,
            _ => JobFileKind.Other
        };
    }

    public static string GetFenceLanguage(string? fileName) => GetKind(fileName) switch
    {
        JobFileKind.CSharp => "csharp",
        JobFileKind.Python => "python",
        JobFileKind.Json => "json",
        JobFileKind.Nuspec => "xml",
        _ => "text"
    };
}
