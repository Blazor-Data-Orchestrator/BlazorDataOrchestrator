using BlazorDataOrchestrator.Core.Services.AI;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests.AI;

public class JobFileContentValidatorTests
{
    private const string AppSettings = """
        {
          "ConnectionStrings": {
            "blobs": "",
            "queues": "",
            "tables": "",
            "blazororchestratordb": ""
          },
          "WeatherApi": { "ApiKey": "" }
        }
        """;

    private const string MainCs = """
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public class BlazorDataOrchestratorJob
        {
            public static async Task<List<string>> ExecuteJob(string appSettings, int a, int b, int c, int d, string w)
            {
                await Task.CompletedTask;
                return new List<string>();
            }
        }
        """;

    [Fact]
    public void ValidAppSettings_Passes()
    {
        var result = JobFileContentValidator.Validate("appsettings.json", AppSettings);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CSharpCode_InJsonFile_IsBlocked()
    {
        var result = JobFileContentValidator.Validate("appsettings.json", MainCs);

        Assert.False(result.IsValid);
        Assert.Contains("C# code cannot be written to appsettings.json", Assert.Single(result.Errors));
    }

    [Fact]
    public void PythonCode_InJsonFile_IsBlocked()
    {
        var result = JobFileContentValidator.Validate("appsettings.Production.json", "import json\n\ndef execute_job(a):\n    return []");

        Assert.False(result.IsValid);
        Assert.Contains("Python code", result.Errors[0]);
    }

    [Theory]
    [InlineData("{ // comment\n \"a\": 1 }")]
    [InlineData("{ \"a\": 1, }")]
    [InlineData("{ 'a': 1 }")]
    [InlineData("{ a: 1 }")]
    [InlineData("{ \"a\": ... }")]
    public void NonStrictJson_IsBlocked_WithLineAndPosition(string json)
    {
        var result = JobFileContentValidator.Validate("appsettings.json", json);

        Assert.False(result.IsValid);
        Assert.Matches(@"line \d+, position \d+", result.Errors[0]);
    }

    [Theory]
    [InlineData("[ 1, 2 ]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void NonObjectRoot_IsBlocked(string json)
    {
        Assert.False(JobFileContentValidator.Validate("appsettings.json", json).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyContent_IsBlocked(string content)
    {
        Assert.False(JobFileContentValidator.Validate("appsettings.json", content).IsValid);
        Assert.False(JobFileContentValidator.Validate("main.cs", content).IsValid);
    }

    [Fact]
    public void RemovingReservedConnectionString_Warns()
    {
        var updated = "{ \"ConnectionStrings\": { \"blobs\": \"\", \"queues\": \"\", \"tables\": \"\" } }";

        var result = JobFileContentValidator.Validate("appsettings.json", updated, previousContent: AppSettings);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("'blazororchestratordb' was removed"));
    }

    [Fact]
    public void GivingReservedConnectionStringAValue_Warns()
    {
        var updated = AppSettings.Replace("\"tables\": \"\"", "\"tables\": \"UseDevelopmentStorage=true\"");

        var result = JobFileContentValidator.Validate("appsettings.json", updated, previousContent: AppSettings);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("'tables' was given a value"));
    }

    [Fact]
    public void UnchangedReservedValue_DoesNotWarn()
    {
        var withValue = AppSettings.Replace("\"tables\": \"\"", "\"tables\": \"UseDevelopmentStorage=true\"");

        var result = JobFileContentValidator.Validate("appsettings.Development.json", withValue, previousContent: withValue);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void JsonInCSharpFile_IsBlocked()
    {
        var result = JobFileContentValidator.Validate("main.cs", AppSettings, isPrimaryCodeFile: true);

        Assert.False(result.IsValid);
        Assert.Contains("JSON cannot be written to main.cs", result.Errors[0]);
    }

    [Fact]
    public void ValidMainCs_Passes_WithoutWarnings()
    {
        var result = JobFileContentValidator.Validate("main.cs", MainCs, isPrimaryCodeFile: true);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CSharpSyntaxErrors_AreWarningsNotErrors()
    {
        var broken = MainCs.Replace("return new List<string>();", "return new List<string>(");

        var result = JobFileContentValidator.Validate("main.cs", broken, isPrimaryCodeFile: true);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("syntax errors"));
    }

    [Fact]
    public void PrimaryCSharpFile_WithoutEntryPoint_Warns()
    {
        var result = JobFileContentValidator.Validate("main.cs", "public class Helper { }", isPrimaryCodeFile: true);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("BlazorDataOrchestratorJob"));
    }

    [Fact]
    public void PythonCode_InCSharpFile_IsBlocked()
    {
        Assert.False(JobFileContentValidator.Validate("main.cs", "import json\n\ndef execute_job(a):\n    return []").IsValid);
    }

    [Fact]
    public void CSharpCode_InPythonFile_IsBlocked()
    {
        Assert.False(JobFileContentValidator.Validate("main.py", MainCs).IsValid);
    }

    [Fact]
    public void PrimaryPythonFile_WithoutExecuteJob_Warns()
    {
        var result = JobFileContentValidator.Validate("main.py", "import json\n\ndef helper():\n    return 1", isPrimaryCodeFile: true);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("execute_job"));
    }

    [Fact]
    public void ValidNuspec_Passes()
    {
        const string nuspec = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <dependencies>
                  <group targetFramework="net10.0">
                    <dependency id="SendGrid" version="9.29.3" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;

        var result = JobFileContentValidator.Validate("BlazorDataOrchestrator.Job.nuspec", nuspec);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("<package><metadata></package>")]
    [InlineData("<project />")]
    public void InvalidNuspec_IsBlocked(string nuspec)
    {
        Assert.False(JobFileContentValidator.Validate("BlazorDataOrchestrator.Job.nuspec", nuspec).IsValid);
    }

    [Fact]
    public void Nuspec_WrongTargetFramework_Warns()
    {
        var result = JobFileContentValidator.Validate("x.nuspec",
            "<package><metadata><dependencies><group targetFramework=\"net8.0\" /></dependencies></metadata></package>");

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("net10.0"));
    }

    [Fact]
    public void Requirements_Valid_Passes()
    {
        var result = JobFileContentValidator.Validate("requirements.txt", "# deps\nrequests==2.31.0\npandas==2.1.4\n");

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Requirements_WithCode_IsBlocked()
    {
        Assert.False(JobFileContentValidator.Validate("requirements.txt", MainCs).IsValid);
    }

    [Fact]
    public void Requirements_UnpinnedVersion_Warns()
    {
        var result = JobFileContentValidator.Validate("requirements.txt", "requests>=2");

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("=="));
    }

    [Fact]
    public void RemovingExistingSettings_Warns()
    {
        var before = "{ \"ConnectionStrings\": { \"blobs\": \"\" }, \"Logging\": { \"LogLevel\": {} }, \"AllowedHosts\": \"*\", \"WeatherApi\": { \"ApiKey\": \"\", \"BaseUrl\": \"x\" } }";
        var after = "{ \"ConnectionStrings\": { \"blobs\": \"\" }, \"WeatherApi\": { \"BaseUrl\": \"y\" } }";

        var result = JobFileContentValidator.Validate("appsettings.json", after, previousContent: before);

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("removes existing settings: Logging, AllowedHosts, WeatherApi:ApiKey", warning);
    }

    [Fact]
    public void KeepingAllSettings_DoesNotWarn_AndIsCaseInsensitive()
    {
        var before = "{ \"WeatherApi\": { \"ApiKey\": \"\" }, \"Feature\": null }";
        var after = "{ \"weatherapi\": { \"apikey\": \"new\", \"BaseUrl\": \"x\" }, \"Feature\": true, \"Added\": 1 }";

        Assert.Empty(JobFileContentValidator.Validate("appsettings.json", after, previousContent: before).Warnings);
    }

    [Fact]
    public void GetJsonError_ReturnsNullForValidJson()
    {
        Assert.Null(JobFileContentValidator.GetJsonError("appsettings.json", AppSettings));
        Assert.NotNull(JobFileContentValidator.GetJsonError("appsettings.json", "{"));
    }
}
