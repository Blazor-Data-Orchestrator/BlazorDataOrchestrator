using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;
using BlazorDataOrchestrator.Core.Configuration;
using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Testing;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class NuGetPackageBuilderTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public async Task RejectsPackageIdThatCouldEscapeOutputDirectory()
    {
        using var scope = new TestRunScope("invalid-package-id");
        var result = await new NuGetPackageBuilderService().BuildPackageAsync(new NuGetPackageBuilderService.PackageBuildConfiguration
        {
            CodeRootPath = scope.RootPath,
            PackageId = "../unsafe"
        });

        Assert.False(result.Success);
        Assert.Contains("Invalid package id", result.ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task RejectsPackageWithoutLanguageEntryPoint()
    {
        using var scope = new TestRunScope("missing-entry-point");
        var result = await new NuGetPackageBuilderService().BuildPackageAsync(new NuGetPackageBuilderService.PackageBuildConfiguration
        {
            CodeRootPath = scope.RootPath,
            PackageId = "BDO.Tests.Empty"
        });

        Assert.False(result.Success);
        Assert.Contains("No code was packaged", result.ErrorMessage);
    }

    private const string SecretSettings = """
        {
          // Comments are allowed, as in ASP.NET configuration.
          "ConnectionStrings": {
            "blobs": "UseDevelopmentStorage=true",
            "Queues": "queue-secret",
            "tables": "table-secret",
            "blazororchestratordb": "Server=127.0.0.1,14330;User ID=sa;Password=YourStrong@Passw0rd",
            "myapi": "Server=custom;Password=kept"
          },
          "Settings": { "ApiKey": "api-key-kept" }
        }
        """;

    [Fact(DisplayName = "S4: all four appsettings files ship with blank reserved connection strings")]
    [Trait("Category", "Contract")]
    public async Task BuildPackage_BlanksReservedConnectionStrings_AllFourFiles()
    {
        using var scope = new TestRunScope("s4-blank");
        var (config, _) = CreateJob(scope, SecretSettings);

        var result = await new NuGetPackageBuilderService().BuildPackageAsync(config);

        Assert.True(result.Success, result.ErrorMessage);
        var files = ReadAppSettings(result.PackagePath!);
        Assert.Equal(JobEnvironments.AllFileNames.Count, files.Count);
        foreach (var (name, json) in files)
        {
            var connectionStrings = JsonNode.Parse(json)!["ConnectionStrings"]!.AsObject();
            foreach (var key in ReservedConnectionStrings.ReservedKeys)
            {
                Assert.Equal(string.Empty, connectionStrings[key]!.GetValue<string>());
            }
            Assert.DoesNotContain("Password=YourStrong", json);
            Assert.DoesNotContain("queue-secret", json);
        }
        Assert.Contains(result.Logs, l => l == "Blanked reserved connection strings in appsettings.Development.json");
    }

    [Fact(DisplayName = "S4: non-reserved settings survive blanking")]
    [Trait("Category", "Contract")]
    public async Task BuildPackage_PreservesNonReservedSettings()
    {
        using var scope = new TestRunScope("s4-preserve");
        var (config, _) = CreateJob(scope, SecretSettings);

        var result = await new NuGetPackageBuilderService().BuildPackageAsync(config);

        Assert.True(result.Success, result.ErrorMessage);
        foreach (var (_, json) in ReadAppSettings(result.PackagePath!))
        {
            var node = JsonNode.Parse(json)!;
            Assert.Equal("Server=custom;Password=kept", node["ConnectionStrings"]!["myapi"]!.GetValue<string>());
            Assert.Equal("api-key-kept", node["Settings"]!["ApiKey"]!.GetValue<string>());
        }
    }

    [Fact(DisplayName = "S4: an appsettings file that is not valid JSON fails the build")]
    [Trait("Category", "Contract")]
    public async Task BuildPackage_InvalidAppSettingsJson_Fails()
    {
        using var scope = new TestRunScope("s4-invalid");
        var (config, root) = CreateJob(scope, SecretSettings);
        File.WriteAllText(Path.Combine(root, "appsettings.Staging.json"), "{ \"ConnectionStrings\": { \"blazororchestratordb\": \"Password=x\" ");

        var result = await new NuGetPackageBuilderService().BuildPackageAsync(config);

        Assert.False(result.Success);
        Assert.Contains("appsettings.Staging.json", result.ErrorMessage);
        Assert.Null(result.PackagePath);
    }

    [Fact(DisplayName = "O6: default EF Core dependencies match the loaded EF Core package version")]
    [Trait("Category", "Contract")]
    public void DefaultDependencies_EfCore_MatchesLoadedAssemblyVersion()
    {
        var expected = typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

        foreach (var id in new[] { "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.SqlServer" })
        {
            var dependency = Assert.Single(NuGetPackageBuilderService.DefaultDependencies, d => d.Id == id);
            Assert.Equal(expected, dependency.Version);
            Assert.NotEqual("10.0.0", dependency.Version);
        }
    }

    private static (NuGetPackageBuilderService.PackageBuildConfiguration Config, string Root) CreateJob(TestRunScope scope, string settings)
    {
        var root = scope.CreateDirectory("job");
        Directory.CreateDirectory(Path.Combine(root, "CodeCSharp"));
        File.WriteAllText(Path.Combine(root, "CodeCSharp", "main.cs"), CanonicalJobSources.Create(JobLanguage.CSharp, false, false, false));

        // A stray copy under Code/ must be blanked too.
        File.WriteAllText(Path.Combine(root, "appsettings.json"), settings);

        var environments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var environment in JobEnvironments.All)
        {
            var path = Path.Combine(root, JobEnvironments.GetFileName(environment));
            File.WriteAllText(path, settings);
            environments[environment] = path;
        }

        var basePath = Path.Combine(scope.CreateDirectory("base"), JobEnvironments.BaseFileName);
        File.WriteAllText(basePath, settings);

        return (new NuGetPackageBuilderService.PackageBuildConfiguration
        {
            CodeRootPath = root,
            PackageId = "BDO.Tests.Blanking",
            Version = "1.0.0",
            AppSettingsPath = basePath,
            EnvironmentAppSettingsPaths = environments,
            Dependencies = new()
        }, root);
    }

    private static List<(string Name, string Json)> ReadAppSettings(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries
            .Where(e => JobEnvironments.AllFileNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
            .Select(e =>
            {
                using var reader = new StreamReader(e.Open());
                return (e.FullName, reader.ReadToEnd());
            })
            .ToList();
    }
}