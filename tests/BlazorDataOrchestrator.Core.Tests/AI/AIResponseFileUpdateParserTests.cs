using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services.AI;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests.AI;

public class AIResponseFileUpdateParserTests
{
    private const string ValidMainCs = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public class BlazorDataOrchestratorJob
        {
            public static async Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId, string webAPIParameter)
            {
                await Task.CompletedTask;
                return new List<string> { "retry" };
            }
        }
        """;

    private const string AppSettingsJson = """
        {
          "ConnectionStrings": {
            "blobs": "",
            "queues": "",
            "tables": "",
            "blazororchestratordb": ""
          }
        }
        """;

    private static AIEditorContext Context(string active = "appsettings.json", string language = "csharp")
    {
        var primary = AIEditorContext.GetPrimaryFileName(language);
        var files = language == "python"
            ? new[] { "main.py", "appsettings.json", "appsettings.Production.json", "requirements.txt" }
            : new[] { "main.cs", "appsettings.json", "appsettings.Development.json", "appsettings.Staging.json", "appsettings.Production.json", "BlazorDataOrchestrator.Job.nuspec" };
        return new AIEditorContext
        {
            Language = language,
            ActiveFileName = active,
            ActiveFileContent = active.EndsWith(".json") ? AppSettingsJson : "// code",
            PrimaryCodeFileName = primary,
            PrimaryCodeContent = "// old code",
            AvailableFiles = files,
            NuspecFileName = language == "python" ? null : "BlazorDataOrchestrator.Job.nuspec",
            NuspecContent = language == "python" ? null : "<package />"
        };
    }

    private static string CodeBlock(string code, string lang = "csharp") =>
        $"###UPDATED CODE BEGIN###\n```{lang}\n{code}\n```\n###UPDATED CODE END###";

    [Fact]
    public void Regression_LegacyMarkersWithJsonActive_TargetMainCs_NotAppSettings()
    {
        var response = "Here is the change.\n\n" + CodeBlock(ValidMainCs);

        var result = AIResponseFileUpdateParser.Parse(response, Context(active: "appsettings.json"));

        var update = Assert.Single(result.Updates);
        Assert.Equal("main.cs", update.FileName);
        Assert.Equal(FileUpdateSource.PrimaryCodeMarkers, update.Source);
        Assert.True(update.IsValid);
        Assert.Contains("class BlazorDataOrchestratorJob", update.Content);
        Assert.DoesNotContain("```", update.Content);
    }

    [Fact]
    public void NamedBlock_TargetsTheNamedFile()
    {
        var response = CodeBlock(ValidMainCs) + "\n\n###UPDATED FILE BEGIN: appsettings.json###\n```json\n" + AppSettingsJson + "\n```\n###UPDATED FILE END###";

        var result = AIResponseFileUpdateParser.Parse(response, Context(active: "main.cs"));

        Assert.Equal(["main.cs", "appsettings.json"], result.Updates.Select(u => u.FileName));
        Assert.All(result.Updates, u => Assert.True(u.IsValid, string.Join(" ", u.Validation.Errors)));
    }

    [Fact]
    public void NamedBlock_ResolvesFileNameCaseInsensitively()
    {
        var response = "###UPDATED FILE BEGIN: APPSETTINGS.PRODUCTION.JSON###\n```json\n{ }\n```\n###UPDATED FILE END###";

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        Assert.Equal("appsettings.Production.json", Assert.Single(result.Updates).FileName);
    }

    [Fact]
    public void NamedBlock_ForUnknownFile_IsDroppedWithWarning()
    {
        var response = "###UPDATED FILE BEGIN: secrets.json###\n```json\n{ }\n```\n###UPDATED FILE END###";

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        Assert.Empty(result.Updates);
        Assert.Contains(result.Warnings, w => w.Contains("secrets.json"));
    }

    [Fact]
    public void CSharpInNamedJsonBlock_IsInvalid()
    {
        var response = "###UPDATED FILE BEGIN: appsettings.json###\n```csharp\n" + ValidMainCs + "\n```\n###UPDATED FILE END###";

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        var update = Assert.Single(result.Updates);
        Assert.False(update.IsValid);
        Assert.Contains(update.Validation.Errors, e => e.Contains("C# code cannot be written to appsettings.json"));
        Assert.False(result.HasApplicableUpdates);
    }

    [Fact]
    public void NuspecOutsideCodeMarkers_IsCaptured()
    {
        var nuspec = "<?xml version=\"1.0\"?>\n<package><metadata><dependencies><group targetFramework=\"net10.0\" /></dependencies></metadata></package>";
        var response = CodeBlock(ValidMainCs) + "\n\n###NUSPEC BEGIN###\n```xml\n" + nuspec + "\n```\n###NUSPEC END###";

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        Assert.Equal(2, result.Updates.Count);
        var nuspecUpdate = result.Updates.Single(u => u.Source == FileUpdateSource.NuspecMarkers);
        Assert.Equal("BlazorDataOrchestrator.Job.nuspec", nuspecUpdate.FileName);
        Assert.True(nuspecUpdate.IsValid, string.Join(" ", nuspecUpdate.Validation.Errors));
    }

    [Fact]
    public void NuspecInsideCodeMarkers_IsSeparatedFromMainCs()
    {
        var response = "###UPDATED CODE BEGIN###\n```csharp\n" + ValidMainCs + "\n```\n###NUSPEC BEGIN###\n```xml\n<package />\n```\n###NUSPEC END###\n###UPDATED CODE END###";

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        var main = result.Updates.Single(u => u.FileName == "main.cs");
        Assert.DoesNotContain("NUSPEC", main.Content);
        Assert.DoesNotContain("<package", main.Content);
        Assert.Contains(result.Updates, u => u.Source == FileUpdateSource.NuspecMarkers);
    }

    [Fact]
    public void NuspecBlock_IsIgnoredForPythonJobs()
    {
        var response = "###NUSPEC BEGIN###\n<package />\n###NUSPEC END###";

        var result = AIResponseFileUpdateParser.Parse(response, Context(language: "python"));

        Assert.Empty(result.Updates);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void DuplicateBlocks_LastWins_WithWarning()
    {
        var response = CodeBlock("// first\npublic class A {}") + "\n" + CodeBlock(ValidMainCs);

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        Assert.Contains("BlazorDataOrchestratorJob", Assert.Single(result.Updates).Content);
        Assert.Contains(result.Warnings, w => w.Contains("2 versions of main.cs"));
    }

    [Fact]
    public void FencedFallback_TargetsPrimaryFile_EvenWhenJsonIsActive()
    {
        var response = "Try this:\n```csharp\n" + ValidMainCs + "\n```";

        var result = AIResponseFileUpdateParser.Parse(response, Context(active: "appsettings.json"));

        var update = Assert.Single(result.Updates);
        Assert.Equal("main.cs", update.FileName);
        Assert.Equal(FileUpdateSource.FencedBlockFallback, update.Source);
    }

    [Fact]
    public void FencedFallback_CanBeDisabled_ForAutoApplyCallers()
    {
        var code = "```csharp\n" + ValidMainCs + "\n```";
        var json = "```json\n" + AppSettingsJson + "\n```";

        Assert.Empty(AIResponseFileUpdateParser.Parse(code, Context(), allowFencedFallback: false).Updates);
        Assert.Empty(AIResponseFileUpdateParser.Parse(json, Context(active: "appsettings.json"), allowFencedFallback: false).Updates);
        Assert.Single(AIResponseFileUpdateParser.Parse(CodeBlock(ValidMainCs), Context(), allowFencedFallback: false).Updates);
    }

    [Fact]
    public void NamedJsonBlock_IsComparedWithOtherFileContents()
    {
        var context = Context(active: "main.cs") with
        {
            OtherFileContents = new Dictionary<string, string>
            {
                ["appsettings.json"] = "{ \"ConnectionStrings\": { \"blobs\": \"\" }, \"Logging\": { \"LogLevel\": {} }, \"WeatherApi\": { \"ApiKey\": \"\" } }"
            }
        };
        var response = "###UPDATED FILE BEGIN: appsettings.json###\n```json\n{ \"ConnectionStrings\": { \"blobs\": \"\" }, \"WeatherApi\": { \"BaseUrl\": \"x\" } }\n```\n###UPDATED FILE END###";

        var update = Assert.Single(AIResponseFileUpdateParser.Parse(response, context).Updates);

        Assert.True(update.IsValid);
        Assert.Contains(update.Validation.Warnings, w => w.Contains("Logging") && w.Contains("WeatherApi:ApiKey"));
    }

    [Fact]
    public void MarkersQuotedMidLine_InACodeExample_AreIgnored()
    {
        // Seen live: the model refused a request and showed the markers inside a C# string literal.
        var response = "I won't send that. You can test it like this:\n```csharp\nvar response = \"###UPDATED FILE BEGIN: appsettings.json###\\n```json\\nusing System;\\n```\\n###UPDATED FILE END###\\n\";\n```";

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        Assert.Empty(result.Updates);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void IndentedMarkers_AtLineStart_AreAccepted()
    {
        var response = "  ###UPDATED CODE BEGIN###\n```csharp\n" + ValidMainCs + "\n```\n  ###UPDATED CODE END###";

        Assert.Equal("main.cs", Assert.Single(AIResponseFileUpdateParser.Parse(response, Context()).Updates).FileName);
    }

    [Fact]
    public void FencedFallback_IgnoresShortSnippets()
    {
        var response = "Use this line:\n```csharp\nvar x = 1;\n```";

        Assert.Empty(AIResponseFileUpdateParser.Parse(response, Context()).Updates);
    }

    [Fact]
    public void FencedFallback_IgnoresOtherLanguageFences()
    {
        var response = "```python\ndef execute_job():\n    pass\n```";

        Assert.Empty(AIResponseFileUpdateParser.Parse(response, Context(language: "csharp")).Updates);
    }

    [Fact]
    public void JsonFenceFallback_OnlyTargetsActiveJsonFile()
    {
        var response = "```json\n" + AppSettingsJson + "\n```";

        var jsonActive = AIResponseFileUpdateParser.Parse(response, Context(active: "appsettings.Production.json"));
        var codeActive = AIResponseFileUpdateParser.Parse(response, Context(active: "main.cs"));

        Assert.Equal("appsettings.Production.json", Assert.Single(jsonActive.Updates).FileName);
        Assert.Empty(codeActive.Updates);
    }

    [Fact]
    public void ProseOnlyResponse_HasNoUpdates()
    {
        var result = AIResponseFileUpdateParser.Parse("The job reads appSettings with JsonSerializer.", Context());

        Assert.Empty(result.Updates);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CrLfResponses_ParseTheSame()
    {
        var response = ("Here.\n\n" + CodeBlock(ValidMainCs)).Replace("\n", "\r\n");

        var update = Assert.Single(AIResponseFileUpdateParser.Parse(response, Context()).Updates);

        Assert.Equal("main.cs", update.FileName);
        Assert.True(update.IsValid);
    }

    [Fact]
    public void UnclosedMarker_ProducesTruncationWarning()
    {
        var response = "###UPDATED CODE BEGIN###\n```csharp\npublic class BlazorDataOrchestratorJob {";

        var result = AIResponseFileUpdateParser.Parse(response, Context());

        Assert.Empty(result.Updates);
        Assert.Contains(result.Warnings, w => w.Contains("truncated"));
    }

    [Fact]
    public void PythonJob_LegacyMarkersTargetMainPy()
    {
        var response = CodeBlock("import json\n\ndef execute_job(app_settings, a, b, c, d, web_api_parameter=\"\"):\n    return []", "python");

        var update = Assert.Single(AIResponseFileUpdateParser.Parse(response, Context(active: "requirements.txt", language: "python")).Updates);

        Assert.Equal("main.py", update.FileName);
        Assert.True(update.IsValid);
    }

    [Fact]
    public void StripMarkersForDisplay_RemovesMarkers_AndLabelsNamedFiles()
    {
        var response = "Intro\n" + CodeBlock("class A {}") + "\n###UPDATED FILE BEGIN: appsettings.json###\n```json\n{}\n```\n###UPDATED FILE END###";

        var display = AIResponseFileUpdateParser.StripMarkersForDisplay(response);

        Assert.DoesNotContain("###", display);
        Assert.Contains("**appsettings.json**", display);
        Assert.Contains("class A {}", display);
    }
}
