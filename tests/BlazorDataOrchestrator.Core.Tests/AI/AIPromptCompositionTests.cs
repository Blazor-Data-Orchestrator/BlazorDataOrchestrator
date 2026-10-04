using Azure.Data.Tables;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;
using BlazorDataOrchestrator.Core.Services.AI;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests.AI;

public class AIPromptCompositionTests
{
    private sealed class FakeInstructionsProvider(string content) : IInstructionsProvider
    {
        public List<string> RequestedLanguages { get; } = [];

        public string GetInstructionsForLanguage(string language)
        {
            RequestedLanguages.Add(language);
            return content;
        }

        public SkillInstructionsInfo GetInfo(string language) =>
            new(SkillResourceNames.ForLanguage(language), content.Length > 0, content.Length, "abc123");
    }

    private static CodeAssistantChatService CreateService(IInstructionsProvider provider) =>
        new(new AISettingsService(new TableServiceClient("UseDevelopmentStorage=true")), provider);

    private static AIEditorContext JsonActiveContext() => new()
    {
        Language = "csharp",
        ActiveFileName = "appsettings.json",
        ActiveFileContent = "{ \"ConnectionStrings\": { \"blobs\": \"\" } }",
        PrimaryCodeFileName = "main.cs",
        PrimaryCodeContent = "public class BlazorDataOrchestratorJob { }",
        AvailableFiles = ["main.cs", "appsettings.json", "appsettings.Production.json", "BlazorDataOrchestrator.Job.nuspec"],
        NuspecFileName = "BlazorDataOrchestrator.Job.nuspec",
        NuspecContent = "<package />"
    };

    [Fact]
    public void SystemPrompt_InjectsSkillContent_BetweenMarkers()
    {
        var provider = new FakeInstructionsProvider("SKILL BODY TEXT");
        var service = CreateService(provider);
        service.SetEditorContext(JsonActiveContext());

        var prompt = service.BuildSystemPrompt();

        Assert.Contains(AIPromptComposer.FileTargetingRules, prompt);
        Assert.Contains("## Project Skill: coding-a-job-csharp", prompt);
        var begin = prompt.IndexOf(AIPromptComposer.SkillBeginMarker, StringComparison.Ordinal);
        var body = prompt.IndexOf("SKILL BODY TEXT", StringComparison.Ordinal);
        var end = prompt.IndexOf(AIPromptComposer.SkillEndMarker, StringComparison.Ordinal);
        Assert.True(begin >= 0 && begin < body && body < end);
    }

    [Fact]
    public void SystemPrompt_UsesTheEditorContextLanguage()
    {
        var provider = new FakeInstructionsProvider("python skill");
        var service = CreateService(provider);
        service.SetEditorContext(AIEditorContext.ForSingleFile("python", "def execute_job(): pass"));

        var prompt = service.BuildSystemPrompt();

        Assert.Contains("## Project Skill: coding-a-job-python", prompt);
        Assert.Equal("python", provider.RequestedLanguages.Last());
    }

    [Fact]
    public void SystemPrompt_KeepsFileTargetingRules_WhenSkillIsMissing()
    {
        var service = CreateService(new FakeInstructionsProvider(""));

        var prompt = service.BuildSystemPrompt();

        Assert.Contains(AIPromptComposer.FileTargetingRules, prompt);
        Assert.DoesNotContain(AIPromptComposer.SkillBeginMarker, prompt);
    }

    [Fact]
    public void SystemPrompt_WithRealSkill_ContainsFullSkill()
    {
        var provider = new SkillInstructionsProvider(typeof(AIPromptCompositionTests).Assembly);
        var service = CreateService(provider);

        var prompt = service.BuildSystemPrompt();

        Assert.Contains(provider.GetInstructionsForLanguage("csharp"), prompt);
    }

    [Fact]
    public void UserMessage_LabelsActiveAndPrimaryFilesSeparately()
    {
        var service = CreateService(new FakeInstructionsProvider("x"));
        service.SetEditorContext(JsonActiveContext());

        var message = service.BuildUserMessageWithContext("Add retry logic");

        Assert.Contains("- main.cs  (PRIMARY code file)", message);
        Assert.Contains("- appsettings.json  (ACTIVE in editor)", message);
        Assert.Contains("### ACTIVE file: appsettings.json (json)\n```json", message.Replace("\r\n", "\n"));
        Assert.Contains("### PRIMARY code file: main.cs (csharp)\n```csharp", message.Replace("\r\n", "\n"));
        Assert.Contains("### NuGet manifest: BlazorDataOrchestrator.Job.nuspec (xml)", message);
        Assert.Contains("Put code changes in main.cs, not in appsettings.json.", message);
        Assert.EndsWith("## User Request:\nAdd retry logic", message.Replace("\r\n", "\n"));
    }

    [Fact]
    public void UserMessage_WhenPrimaryIsActive_ShowsItOnce()
    {
        var context = JsonActiveContext() with { ActiveFileName = "main.cs", ActiveFileContent = "// buffer" };

        var message = EditorContextFormatter.Format(context, "hi");

        Assert.Contains("### PRIMARY code file, ACTIVE in editor: main.cs (csharp)", message);
        Assert.Contains("// buffer", message);
        Assert.DoesNotContain("### ACTIVE file:", message);
        Assert.Contains("- main.cs  (PRIMARY code file, ACTIVE in editor)", message);
    }

    [Fact]
    public void UserMessage_WhenNuspecIsActive_DoesNotDuplicateIt()
    {
        var context = JsonActiveContext() with
        {
            ActiveFileName = "BlazorDataOrchestrator.Job.nuspec",
            ActiveFileContent = "<package />"
        };

        var message = EditorContextFormatter.Format(context, "hi");

        Assert.Contains("### ACTIVE file: BlazorDataOrchestrator.Job.nuspec (xml)", message);
        Assert.DoesNotContain("### NuGet manifest:", message);
    }

    [Fact]
    public void UserMessage_IncludesOtherJobFiles_SoTheyAreEditedNotRecreated()
    {
        var context = JsonActiveContext() with
        {
            ActiveFileName = "main.cs",
            ActiveFileContent = "// buffer",
            OtherFileContents = new Dictionary<string, string>
            {
                ["appsettings.json"] = "{ \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } } }",
                ["main.cs"] = "should be skipped (primary)",
                ["BlazorDataOrchestrator.Job.nuspec"] = "should be skipped (nuspec)"
            }
        };

        var message = EditorContextFormatter.Format(context, "hi");

        Assert.Contains("### Other job file: appsettings.json (json)", message);
        Assert.Contains("\"Logging\"", message);
        Assert.Contains("keep every existing setting", message);
        Assert.DoesNotContain("should be skipped", message);
    }

    [Fact]
    public void UserMessage_ListsButOmitsVeryLargeOtherFiles()
    {
        var context = JsonActiveContext() with
        {
            OtherFileContents = new Dictionary<string, string>
            {
                ["appsettings.Production.json"] = "{ \"a\": \"" + new string('x', EditorContextFormatter.MaxOtherFileChars) + "\" }"
            }
        };

        var message = EditorContextFormatter.Format(context, "hi");

        Assert.Contains("appsettings.Production.json", message);
        Assert.Contains("too large to include", message);
        Assert.DoesNotContain(new string('x', 100), message);
    }

    [Fact]
    public void ObsoleteSetters_StillProvideSingleFileContext()
    {
        var service = CreateService(new FakeInstructionsProvider("x"));
#pragma warning disable CS0618
        service.SetLanguage("python");
        service.SetCurrentEditorCode("def execute_job(): pass");
#pragma warning restore CS0618

        Assert.Equal("python", service.EditorContext.Language);
        Assert.Equal("main.py", service.EditorContext.ActiveFileName);
        Assert.True(service.EditorContext.ActiveIsPrimary);
    }
}
