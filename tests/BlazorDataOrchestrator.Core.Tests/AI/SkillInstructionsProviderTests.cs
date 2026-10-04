using System.Reflection;
using BlazorDataOrchestrator.Core.Services;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests.AI;

public class SkillInstructionsProviderTests
{
    private static readonly Assembly TestAssembly = typeof(SkillInstructionsProviderTests).Assembly;

    [Fact]
    public void LoadsCSharpSkill_WithoutFrontMatter()
    {
        var provider = new SkillInstructionsProvider(TestAssembly);

        var content = provider.GetInstructionsForLanguage("csharp");

        Assert.StartsWith("<!--", content);
        Assert.Contains("# Blazor Data Orchestrator C# Instructions", content);
        Assert.DoesNotContain("name: coding-a-job-csharp", content);
    }

    [Theory]
    [InlineData("python")]
    [InlineData("py")]
    [InlineData(" Python ")]
    public void MapsPythonAliases_ToPythonSkill(string language)
    {
        var provider = new SkillInstructionsProvider(TestAssembly);

        Assert.Contains("# Blazor Data Orchestrator Python Instructions", provider.GetInstructionsForLanguage(language));
        Assert.Equal(SkillResourceNames.Python, provider.GetInfo(language).ResourceName);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("c#")]
    [InlineData("")]
    [InlineData("unknown")]
    public void FallsBackToCSharp_ForOtherLanguages(string language)
    {
        var provider = new SkillInstructionsProvider(TestAssembly);

        Assert.Equal(SkillResourceNames.CSharp, provider.GetInfo(language).ResourceName);
    }

    [Theory]
    [InlineData("csharp", "main.cs")]
    [InlineData("python", "main.py")]
    public void ShippedSkills_ContainFileTargetingAndStrictJsonRules(string language, string primaryFile)
    {
        var content = new SkillInstructionsProvider(TestAssembly).GetInstructionsForLanguage(language);

        Assert.Contains("## 0. File Targeting and File-Type Rules", content);
        Assert.Contains("###UPDATED FILE BEGIN:", content);
        Assert.Contains("strictly valid JSON", content);
        Assert.Contains($"change **`{primaryFile}`**", content);
        Assert.DoesNotContain("\"appsettingsProduction.json\"", content);
    }

    [Fact]
    public void CSharpSkill_UsesOneNuGetHeaderSyntax()
    {
        var content = new SkillInstructionsProvider(TestAssembly).GetInstructionsForLanguage("csharp");

        Assert.DoesNotContain("REQUIRES NUGET", content);
        Assert.Contains("// NUGET: <PackageId>, <Version>", content);
    }

    [Fact]
    public void GetInfo_ReportsLengthAndHash()
    {
        var info = new SkillInstructionsProvider(TestAssembly).GetInfo("csharp");

        Assert.True(info.Found);
        Assert.True(info.Length > 1000);
        Assert.Equal(12, info.Sha256.Length);
    }

    [Fact]
    public void MissingResource_ReturnsEmptyAndNotFound()
    {
        var provider = new SkillInstructionsProvider(typeof(object).Assembly);

        Assert.Equal(string.Empty, provider.GetInstructionsForLanguage("csharp"));
        Assert.False(provider.GetInfo("csharp").Found);
    }

    [Theory]
    [InlineData("---\nname: x\ndescription: y\n---\n# Title\nBody", "# Title\nBody")]
    [InlineData("---\r\nname: x\r\n---\r\n# Title", "# Title")]
    [InlineData("\uFEFF---\nname: x\n---\n# Title", "# Title")]
    [InlineData("# Title\n---\nnot front matter\n---\n", "# Title\n---\nnot front matter\n---\n")]
    public void StripFrontMatter_RemovesOnlyLeadingBlock(string input, string expected)
    {
        Assert.Equal(expected, SkillInstructionsProvider.StripFrontMatter(input));
    }
}
