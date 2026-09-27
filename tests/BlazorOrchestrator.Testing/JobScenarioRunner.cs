using System.Text.Json;
using BlazorDataOrchestrator.Core.Configuration;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Testing;

public sealed class JobScenarioRunner
{
    public async Task<ScenarioRunResult> RunAsync(JobScenario scenario)
    {
        using var scope = new TestRunScope(scenario.Id);
        var ai = new ScriptedAiService();
        string? initialCode = null;

        if (scenario.IsUpdate)
        {
            initialCode = CanonicalJobSources.Create(scenario.Language, false, scenario.HasDependency, false);
            await BuildAndInspectAsync(scope, scenario with { IsUpdate = false, UsesAi = false }, initialCode, "initial");
        }

        var source = scenario.UsesAi
            ? ai.Generate(scenario, initialCode)
            : CanonicalJobSources.Create(scenario.Language, scenario.IsUpdate, scenario.HasDependency, false);
        await BuildAndInspectAsync(scope, scenario, source, "current");

        if (scenario.UsesAi && ai.Requests.Count != 1)
        {
            throw new InvalidOperationException("AI scenario did not make exactly one scripted request.");
        }

        if (!scenario.UsesAi && ai.Requests.Count != 0)
        {
            throw new InvalidOperationException("Manual scenario unexpectedly invoked AI.");
        }

        if (scenario.IsUpdate && scenario.UsesAi && ai.Requests[0].CurrentCode != initialCode)
        {
            throw new InvalidOperationException("AI update did not receive the existing source.");
        }

        return new ScenarioRunResult(scope.RunId, scenario.ExpectedToken, ai.Requests.Count);
    }

    private static async Task BuildAndInspectAsync(TestRunScope scope, JobScenario scenario, string source, string phase)
    {
        var codeRoot = scope.CreateDirectory($"{scenario.Surface}-{phase}");
        var languageFolder = Path.Combine(codeRoot, scenario.Language == JobLanguage.CSharp ? "CodeCSharp" : "CodePython");
        Directory.CreateDirectory(languageFolder);
        await File.WriteAllTextAsync(Path.Combine(languageFolder, scenario.Language == JobLanguage.CSharp ? "main.cs" : "main.py"), source);

        var dependencies = new List<PackageDependency>();
        if (scenario.Language == JobLanguage.CSharp && scenario.HasDependency)
        {
            dependencies.Add(new PackageDependency { Id = CanonicalJobSources.CSharpDependencyId, Version = CanonicalJobSources.CSharpDependencyVersion });
            await File.WriteAllTextAsync(Path.Combine(languageFolder, "dependencies.json"), JsonSerializer.Serialize(new DependenciesConfig { Dependencies = dependencies }));
        }
        else if (scenario.Language == JobLanguage.Python && scenario.HasDependency)
        {
            await File.WriteAllTextAsync(Path.Combine(languageFolder, "requirements.txt"), CanonicalJobSources.PythonDependency + Environment.NewLine);
        }

        await File.WriteAllTextAsync(Path.Combine(codeRoot, "configuration.json"), JsonSerializer.Serialize(new
        {
            SelectedLanguage = scenario.Language == JobLanguage.CSharp ? "csharp" : "python",
            LastJobId = 0,
            LastJobInstanceId = 0
        }));

        var appSettingsPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var basePath = Path.Combine(codeRoot, JobEnvironments.BaseFileName);
        await File.WriteAllTextAsync(basePath, EmptyAppSettings);
        foreach (var environment in JobEnvironments.All)
        {
            var path = Path.Combine(codeRoot, JobEnvironments.GetFileName(environment));
            await File.WriteAllTextAsync(path, EmptyAppSettings);
            appSettingsPaths[environment] = path;
        }

        var builder = new NuGetPackageBuilderService();
        var package = await builder.BuildPackageAsStreamAsync(new NuGetPackageBuilderService.PackageBuildConfiguration
        {
            CodeRootPath = codeRoot,
            PackageId = $"BDO.Tests.{scenario.Id.Replace('-', '.')}" + (scenario.Surface == JobSurface.VisualStudio ? ".VS" : ".Web"),
            Version = scenario.IsUpdate ? "1.0.1" : "1.0.0",
            AppSettingsPath = basePath,
            EnvironmentAppSettingsPaths = appSettingsPaths,
            Dependencies = dependencies
        }) ?? throw new InvalidOperationException("Package builder returned no package.");

        await using var packageStream = package.PackageStream;
        using var inspector = new PackageArtifactInspector(packageStream);
        inspector.AssertScenario(scenario);
    }

    private const string EmptyAppSettings = """
        {
          "ConnectionStrings": {
            "blobs": "",
            "queues": "",
            "tables": "",
            "blazororchestratordb": ""
          }
        }
        """;
}

public sealed record ScenarioRunResult(string RunId, string ResultToken, int AiRequestCount);