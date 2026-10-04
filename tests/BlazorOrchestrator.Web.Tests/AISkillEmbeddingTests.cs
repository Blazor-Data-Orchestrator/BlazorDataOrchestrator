using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Testing;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>
/// The AI Code Assistant injects the .github/skills SKILL.md files into its system prompt.
/// These tests keep the embedded copies identical to the repo files and protect the build layout.
/// </summary>
public class AISkillEmbeddingTests
{
    private static readonly System.Reflection.Assembly WebAssembly = typeof(BlazorOrchestrator.Web.Services.JobCodeEditorService).Assembly;

    [Theory(DisplayName = "The web assembly embeds the current SKILL.md files")]
    [Trait("Category", "Contract")]
    [InlineData("coding-a-job-csharp")]
    [InlineData("coding-a-job-python")]
    public void WebAssembly_EmbedsCurrentSkillFiles(string skill)
    {
        using var stream = WebAssembly.GetManifestResourceStream($"Skills/{skill}/SKILL.md");
        Assert.NotNull(stream);

        using var buffer = new MemoryStream();
        stream!.CopyTo(buffer);
        var onDisk = File.ReadAllBytes(Path.Combine(RepoPaths.Root, ".github", "skills", skill, "SKILL.md"));
        Assert.True(onDisk.AsSpan().SequenceEqual(buffer.ToArray()), $"Embedded {skill}/SKILL.md differs from the repo file.");
    }

    [Theory(DisplayName = "The web DI provider serves each language's skill")]
    [Trait("Category", "Contract")]
    [InlineData("csharp", "# Blazor Data Orchestrator C# Instructions")]
    [InlineData("python", "# Blazor Data Orchestrator Python Instructions")]
    public void SkillInstructionsProvider_ReadsWebAssemblySkills(string language, string heading)
    {
        var provider = new SkillInstructionsProvider(WebAssembly);

        Assert.Contains(heading, provider.GetInstructionsForLanguage(language));
        Assert.True(provider.GetInfo(language).Found);
    }

    [Fact(DisplayName = "Core never references .github (the Agent Dockerfile builds Core from src/)")]
    [Trait("Category", "Contract")]
    public void CoreProject_DoesNotReferenceGithubFolder()
    {
        var csproj = File.ReadAllText(RepoPaths.Src("BlazorDataOrchestrator.Core", "BlazorDataOrchestrator.Core.csproj"));

        Assert.DoesNotContain(".github", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("instructions.md", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "No stale *.instructions.md copies remain in src")]
    [Trait("Category", "Contract")]
    public void Source_HasNoStaleInstructionCopies()
    {
        var stale = Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), "*.instructions.md", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .ToList();

        Assert.Empty(stale);
    }
}
