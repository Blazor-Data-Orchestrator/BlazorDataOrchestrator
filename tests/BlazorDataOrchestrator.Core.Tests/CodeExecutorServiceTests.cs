using System.Reflection;
using System.Runtime.Loader;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class CodeExecutorServiceTests
{
    // Not provided by the host (unlike Humanizer.Core, which Roslyn brings in), so it exercises the NuGet load path.
    private const string NuGetPackageId = "CsvHelper";
    private const string NuGetPackageVersion = "33.0.1";

    private const string CsvJob = """
        // NUGET: CsvHelper, 33.0.1
        using System.Collections.Generic;
        using System.Globalization;
        using System.Threading.Tasks;
        using CsvHelper.Configuration;

        public class BlazorDataOrchestratorJob
        {
            public static Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId, string webAPIParameter)
            {
                var configuration = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = "|" };
                return Task.FromResult(new List<string> { "CSV_DELIMITER=" + configuration.Delimiter });
            }
        }
        """;

    [Fact(DisplayName = "S1: a failed compile that loaded a NuGet assembly does not break the next job")]
    [Trait("Category", "Integration")]
    public async Task CSharp_FailedCompileWithNuGet_DoesNotBreakNextJob()
    {
        using var scope = new TestRunScope("s1-failed-compile");
        var executor = CreateExecutor();

        var broken = CreateCSharpPackage(scope, "broken", NuGetPackageId, NuGetPackageVersion,
            ("main.cs", CsvJob.Replace("return Task.FromResult", "return Task.FromResult(;")));
        var first = await executor.ExecuteAsync(broken, new JobExecutionContext());
        executor.CleanupAfterExecution();
        Assert.False(first.Success);

        var valid = CreateCSharpPackage(scope, "valid", NuGetPackageId, NuGetPackageVersion, ("main.cs", CsvJob));
        var second = await executor.ExecuteAsync(valid, new JobExecutionContext());
        executor.CleanupAfterExecution();

        Assert.True(second.Success, second.ErrorMessage + "\n" + string.Join("\n", second.Logs));
        Assert.Contains("CSV_DELIMITER=|", second.Logs);
        Assert.DoesNotContain(second.Logs, l => l.Contains("CsvHelper.dll (already provided by host)"));

        // The successful run loaded the package into the default context; the next job reuses it.
        var third = await executor.ExecuteAsync(valid, new JobExecutionContext());
        executor.CleanupAfterExecution();

        Assert.True(third.Success, third.ErrorMessage + "\n" + string.Join("\n", third.Logs));
        Assert.Contains("CSV_DELIMITER=|", third.Logs);
        Assert.Contains(third.Logs, l => l.Contains("= Reusing CsvHelper.dll (loaded by an earlier job)"));
    }

    [Fact(DisplayName = "O5: dynamic compiles on the CS-Script path")]
    [Trait("Category", "Integration")]
    public async Task CSharp_DynamicKeyword_CompilesOnCsScriptPath()
    {
        using var scope = new TestRunScope("o5-dynamic-csscript");
        var result = await CreateExecutor().ExecuteAsync(
            CreateCSharpPackage(scope, "job", null, null, ("main.cs", DynamicJob)),
            new JobExecutionContext());

        Assert.True(result.Success, result.ErrorMessage + "\n" + string.Join("\n", result.Logs));
        Assert.Contains("DYNAMIC=42", result.Logs);
    }

    [Fact(DisplayName = "O5: dynamic compiles on the Roslyn (multi-file) path")]
    [Trait("Category", "Integration")]
    public async Task CSharp_DynamicKeyword_CompilesOnRoslynPath()
    {
        using var scope = new TestRunScope("o5-dynamic-roslyn");
        var result = await CreateExecutor().ExecuteAsync(
            CreateCSharpPackage(scope, "job", null, null,
                ("main.cs", DynamicJob),
                ("Helper.cs", "public static class Helper { public static int Answer => 42; }")),
            new JobExecutionContext());

        Assert.True(result.Success, result.ErrorMessage + "\n" + string.Join("\n", result.Logs));
        Assert.Contains(result.Logs, l => l.Contains("Roslyn compilation"));
        Assert.Contains("DYNAMIC=42", result.Logs);
    }

    [Fact(DisplayName = "S1: the host catalog lists Core and EF Core")]
    [Trait("Category", "Contract")]
    public void HostAssemblyCatalog_ContainsCoreAndEfCore()
    {
        Assert.True(HostAssemblyCatalog.IsHostProvided("BlazorDataOrchestrator.Core"));
        Assert.True(HostAssemblyCatalog.IsHostProvided("Microsoft.EntityFrameworkCore"));
    }

    [Fact(DisplayName = "S1: the host catalog ignores assemblies loaded after startup")]
    [Trait("Category", "Contract")]
    public void HostAssemblyCatalog_DoesNotContainJobLoadedAssemblies()
    {
        using var scope = new TestRunScope("s1-catalog");
        var name = $"LateLoaded_{Guid.NewGuid():N}";
        var path = Path.Combine(scope.RootPath, name + ".dll");
        var compilation = CSharpCompilation.Create(name,
            new[] { CSharpSyntaxTree.ParseText("public static class Marker { }", cancellationToken: TestContext.Current.CancellationToken) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.True(compilation.Emit(path, cancellationToken: TestContext.Current.CancellationToken).Success);

        var loaded = Assembly.LoadFrom(path);

        Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), a => a == loaded);
        Assert.False(HostAssemblyCatalog.IsHostProvided(name));
    }

    [Fact(DisplayName = "O3: a printed-and-returned message is logged once")]
    [Trait("Category", "Contract")]
    public void MergeReturnedPythonLogs_PrintedAndReturned_LoggedOnce()
    {
        using var scope = new TestRunScope("o3-merge");
        var resultFile = Path.Combine(scope.RootPath, "result.json");
        File.WriteAllText(resultFile, """["Job started", "only returned", ""]""");
        var stdout = new List<string> { "[2026-01-01 00:00:00] [Info] Job started" };
        var logs = new List<string>(stdout);

        CodeExecutorService.MergeReturnedPythonLogs(resultFile, stdout, logs);

        Assert.Equal(new[] { "[2026-01-01 00:00:00] [Info] Job started", "only returned" }, logs);
    }

    [Fact(DisplayName = "O3: a non-list return value is reported, not logged")]
    [Trait("Category", "Contract")]
    public void MergeReturnedPythonLogs_NonList_Warns()
    {
        using var scope = new TestRunScope("o3-merge-invalid");
        var resultFile = Path.Combine(scope.RootPath, "result.json");
        File.WriteAllText(resultFile, "\"not a list\"");
        var logs = new List<string>();

        CodeExecutorService.MergeReturnedPythonLogs(resultFile, Array.Empty<string>(), logs);
        CodeExecutorService.MergeReturnedPythonLogs(Path.Combine(scope.RootPath, "missing.json"), Array.Empty<string>(), logs);

        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.StartsWith("Warning:", l));
    }

    [Fact(DisplayName = "O2: Python jobs receive the web API parameter")]
    [Trait("Category", "Integration")]
    public async Task Python_WebApiParameter_IsPassed()
    {
        SkipWithoutPython();
        using var scope = new TestRunScope("o2-python-param");
        var result = await CreateExecutor().ExecuteAsync(
            CreatePythonPackage(scope, """
                def execute_job(app_settings, job_agent_id, job_id, job_instance_id, job_schedule_id, web_api_parameter=""):
                    return ["PARAM=" + web_api_parameter]
                """),
            new JobExecutionContext { SelectedLanguage = "Python", WebAPIParameter = "Seattle" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("PARAM=Seattle", result.Logs);
    }

    [Fact(DisplayName = "O2: five-parameter Python jobs still run")]
    [Trait("Category", "Integration")]
    public async Task Python_FiveParameterSignature_StillRuns()
    {
        SkipWithoutPython();
        using var scope = new TestRunScope("o2-python-five");
        var result = await CreateExecutor().ExecuteAsync(
            CreatePythonPackage(scope, """
                def execute_job(app_settings, job_agent_id, job_id, job_instance_id, job_schedule_id):
                    return ["FIVE_OK"]
                """),
            new JobExecutionContext { SelectedLanguage = "Python", WebAPIParameter = "ignored" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("FIVE_OK", result.Logs);
    }

    [Fact(DisplayName = "O3: printed-and-returned Python messages are logged once")]
    [Trait("Category", "Integration")]
    public async Task Python_PrintedAndReturnedMessage_LoggedOnce()
    {
        SkipWithoutPython();
        using var scope = new TestRunScope("o3-python-once");
        var result = await CreateExecutor().ExecuteAsync(
            CreatePythonPackage(scope, """
                def execute_job(app_settings, job_agent_id, job_id, job_instance_id, job_schedule_id, web_api_parameter=""):
                    print("[2026-01-01 00:00:00] [Info] ONCE")
                    return ["ONCE"]
                """),
            new JobExecutionContext { SelectedLanguage = "Python" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Single(result.Logs, l => l.EndsWith("ONCE", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "O3: returned-only Python messages are still logged")]
    [Trait("Category", "Integration")]
    public async Task Python_ReturnedOnlyMessage_StillLogged()
    {
        SkipWithoutPython();
        using var scope = new TestRunScope("o3-python-returned");
        var result = await CreateExecutor().ExecuteAsync(
            CreatePythonPackage(scope, """
                def execute_job(app_settings, job_agent_id, job_id, job_instance_id, job_schedule_id, web_api_parameter=""):
                    return ["RETURNED_ONLY"]
                """),
            new JobExecutionContext { SelectedLanguage = "Python" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Single(result.Logs, l => l == "RETURNED_ONLY");
    }

    private const string DynamicJob = """
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public class BlazorDataOrchestratorJob
        {
            public static Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId, string webAPIParameter)
            {
                dynamic value = 40;
                value += 2;
                return Task.FromResult(new List<string> { "DYNAMIC=" + value });
            }
        }
        """;

    private static void SkipWithoutPython() =>
        Assert.SkipUnless(PythonLocator.TryLocate(out _, out var reason), $"No Python 3 interpreter ({reason}).");

    private static CodeExecutorService CreateExecutor() => new(new PackageProcessorService(null!));

    private static string CreateCSharpPackage(TestRunScope scope, string name, string? dependencyId, string? dependencyVersion,
        params (string FileName, string Source)[] files)
    {
        var root = scope.CreateDirectory(name);
        var codeFolder = Path.Combine(root, "contentFiles", "any", "any", "CodeCSharp");
        Directory.CreateDirectory(codeFolder);
        foreach (var (fileName, source) in files)
        {
            File.WriteAllText(Path.Combine(codeFolder, fileName), source);
        }

        File.WriteAllText(Path.Combine(root, "contentFiles", "any", "any", "configuration.json"), """{"SelectedLanguage":"CSharp"}""");
        var dependency = dependencyId == null ? string.Empty : $"""<dependency id="{dependencyId}" version="{dependencyVersion}" />""";
        File.WriteAllText(Path.Combine(root, "Job.nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>Job</id>
                <version>1.0.0</version>
                <dependencies><group targetFramework="net10.0">{dependency}</group></dependencies>
              </metadata>
            </package>
            """);
        return root;
    }

    private static string CreatePythonPackage(TestRunScope scope, string mainPy)
    {
        var root = scope.CreateDirectory("python");
        var codeFolder = Path.Combine(root, "contentFiles", "any", "any", "CodePython");
        Directory.CreateDirectory(codeFolder);
        File.WriteAllText(Path.Combine(codeFolder, "main.py"), mainPy);
        File.WriteAllText(Path.Combine(root, "contentFiles", "any", "any", "configuration.json"), """{"SelectedLanguage":"Python"}""");
        return root;
    }
}
