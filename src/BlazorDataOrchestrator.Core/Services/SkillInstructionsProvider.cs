using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Loads AI coding instructions from the SKILL.md files embedded in a host assembly.
/// Hosts (Web, JobCreatorTemplate) embed the files because Core is also built by the
/// Agent Dockerfile, whose build context (src/) cannot see the repo's .github folder.
/// </summary>
public sealed partial class SkillInstructionsProvider : IInstructionsProvider
{
    private readonly Assembly _resourceAssembly;
    private readonly ILogger<SkillInstructionsProvider>? _logger;
    private readonly ConcurrentDictionary<string, (string Content, SkillInstructionsInfo Info)> _cache = new();

    public SkillInstructionsProvider(Assembly resourceAssembly, ILogger<SkillInstructionsProvider>? logger = null)
    {
        _resourceAssembly = resourceAssembly ?? throw new ArgumentNullException(nameof(resourceAssembly));
        _logger = logger;
    }

    public string GetInstructionsForLanguage(string language) =>
        Load(SkillResourceNames.ForLanguage(language)).Content;

    public SkillInstructionsInfo GetInfo(string language) =>
        Load(SkillResourceNames.ForLanguage(language)).Info;

    private (string Content, SkillInstructionsInfo Info) Load(string resourceName) =>
        _cache.GetOrAdd(resourceName, name =>
        {
            using var stream = _resourceAssembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                _logger?.LogError(
                    "AI skill resource {Resource} is not embedded in {Assembly}. The AI will run without project instructions.",
                    name, _resourceAssembly.GetName().Name);
                return (string.Empty, new SkillInstructionsInfo(name, false, 0, string.Empty));
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = StripFrontMatter(reader.ReadToEnd()).Trim();
            var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12].ToLowerInvariant();

            _logger?.LogInformation("Loaded AI skill {Resource} ({Length} chars, sha256 {Sha})", name, content.Length, sha);
            return (content, new SkillInstructionsInfo(name, true, content.Length, sha));
        });

    /// <summary>Removes a leading YAML front matter block (Copilot skill metadata, not instructions).</summary>
    internal static string StripFrontMatter(string markdown) =>
        FrontMatterRegex().Replace(markdown, string.Empty, 1);

    [GeneratedRegex(@"\A\uFEFF?\s*---\r?\n.*?\r?\n---[ \t]*(\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
}
