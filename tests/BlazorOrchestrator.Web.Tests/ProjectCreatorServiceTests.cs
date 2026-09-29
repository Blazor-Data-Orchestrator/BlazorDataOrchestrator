using System.IO.Compression;
using System.Text.Json.Nodes;
using BlazorOrchestrator.Testing;
using BlazorOrchestrator.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

public class ProjectCreatorServiceTests
{
    [Fact(DisplayName = "Visual Studio project export routes code and sanitizes filenames")]
    [Trait("Category", "Integration")]
    public async Task CreateProjectZipRoutesFilesAndSanitizesPackagePaths()
    {
        using var scope = new TestRunScope("project-export");
        var contentRoot = scope.CreateDirectory("web-root");
        var templateDirectory = Path.Combine(contentRoot, "JobTemplate");
        Directory.CreateDirectory(templateDirectory);
        var templateZip = Path.Combine(templateDirectory, "BlazorDataOrchestrator.JobCreatorTemplate.zip");
        CreateTemplate(templateZip, scope.RootPath);

        var service = new ProjectCreatorService(
            new TestWebHostEnvironment(contentRoot),
            NullLogger<ProjectCreatorService>.Instance);
        var archiveBytes = await service.CreateProjectZipAsync("ScenarioProject", new Dictionary<string, string>
        {
            ["../main.cs"] = "CS_CREATE_SIMPLE",
            ["main.py"] = "PY_CREATE_SIMPLE",
            ["dependencies.json"] = "{}",
            ["requirements.txt"] = "packaging==25.0",
            ["configuration.json"] = "{\"SelectedLanguage\":\"csharp\"}"
        });

        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();
        Assert.Contains(entries, entry => entry.EndsWith("/Code/CodeCSharp/main.cs", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("/Code/CodeCSharp/dependencies.json", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("/Code/CodePython/main.py", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("/Code/CodePython/requirements.txt", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("/Code/configuration.json", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Contains("..", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Contains("JobCreatorTemplate", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "O1: VS project export points configuration.json at the platform job")]
    [Trait("Category", "Integration")]
    public async Task CreateProjectZip_WithJobId_RewritesConfiguration()
    {
        using var scope = new TestRunScope("project-export-jobid");
        var contentRoot = scope.CreateDirectory("web-root");
        Directory.CreateDirectory(Path.Combine(contentRoot, "JobTemplate"));
        CreateTemplate(Path.Combine(contentRoot, "JobTemplate", "BlazorDataOrchestrator.JobCreatorTemplate.zip"), scope.RootPath);

        var service = new ProjectCreatorService(new TestWebHostEnvironment(contentRoot), NullLogger<ProjectCreatorService>.Instance);
        var archiveBytes = await service.CreateProjectZipAsync("Weather", new Dictionary<string, string>
        {
            ["main.py"] = "PY",
            // Carries the ID of whichever job last uploaded the package.
            ["configuration.json"] = "{\"SelectedLanguage\":\"python\",\"LastJobId\":77,\"LastJobInstanceId\":900}"
        }, jobId: 12);

        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries, e => e.FullName.Replace('\\', '/').EndsWith("/Code/configuration.json", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        var configuration = JsonNode.Parse(reader.ReadToEnd())!;
        Assert.Equal(12, configuration["LastJobId"]!.GetValue<int>());
        Assert.Equal(0, configuration["LastJobInstanceId"]!.GetValue<int>());
        Assert.Equal("python", configuration["SelectedLanguage"]!.GetValue<string>());
    }

    [Fact(DisplayName = "VS project export adds the job's NuGet packages to the .csproj")]
    [Trait("Category", "Integration")]
    public async Task CreateProjectZip_AddsJobDependenciesToCsproj()
    {
        using var scope = new TestRunScope("project-export-deps");
        var contentRoot = scope.CreateDirectory("web-root");
        Directory.CreateDirectory(Path.Combine(contentRoot, "JobTemplate"));
        CreateTemplate(
            Path.Combine(contentRoot, "JobTemplate", "BlazorDataOrchestrator.JobCreatorTemplate.zip"),
            scope.RootPath,
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <ItemGroup>
                <PackageReference Include="Radzen.Blazor" Version="*" />
                <PackageReference Include="Azure.Storage.Blobs" Version="12.29.2" />
              </ItemGroup>

            </Project>
            """);

        var service = new ProjectCreatorService(new TestWebHostEnvironment(contentRoot), NullLogger<ProjectCreatorService>.Instance);
        var archiveBytes = await service.CreateProjectZipAsync("CsvJob", new Dictionary<string, string>
        {
            ["main.cs"] = "CS",
            ["BlazorDataOrchestrator.Job.nuspec"] = """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>BlazorDataOrchestrator.Job</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework="net10.0">
                        <dependency id="CsvHelper" version="33.0.1" />
                        <dependency id="Microsoft.EntityFrameworkCore" version="10.0.0" />
                        <dependency id="Azure.Storage.Blobs" version="12.1.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """,
            ["dependencies.json"] = """
                { "dependencies": [
                  { "id": "CsvHelper", "version": "1.0.0" },
                  { "id": "Humanizer.Core", "version": "2.14.1" },
                  { "id": "Aspire.Hosting", "version": "13.5.4" }
                ] }
                """
        });

        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
        Assert.DoesNotContain(entries, e => e.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        var csprojEntry = Assert.Single(archive.Entries, e => e.FullName.EndsWith(".csproj", StringComparison.Ordinal));
        using var reader = new StreamReader(csprojEntry.Open());
        var references = System.Xml.Linq.XDocument.Parse(reader.ReadToEnd())
            .Descendants("PackageReference")
            .ToDictionary(r => r.Attribute("Include")!.Value, r => r.Attribute("Version")!.Value);

        Assert.Equal("33.0.1", references["CsvHelper"]);
        Assert.Equal("2.14.1", references["Humanizer.Core"]);
        Assert.Equal("12.29.2", references["Azure.Storage.Blobs"]);
        Assert.False(references.ContainsKey("Microsoft.EntityFrameworkCore"));
        Assert.False(references.ContainsKey("Aspire.Hosting"));
        Assert.Equal(4, references.Count);
    }

    [Fact(DisplayName = "S3: backslash entry names extract as folders")]
    [Trait("Category", "Contract")]
    public void ExtractTemplateNormalized_BackslashZip_CreatesFolders()
    {
        using var scope = new TestRunScope("extract-backslash");
        var zipPath = Path.Combine(scope.RootPath, "backslash.zip");
        CreateZip(zipPath, @"a\b\c.txt", "content");
        var output = scope.CreateDirectory("out");

        ProjectCreatorService.ExtractTemplateNormalized(zipPath, output);

        var extracted = Path.Combine(output, "a", "b", "c.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("content", File.ReadAllText(extracted));
        Assert.Single(Directory.GetFiles(output, "*", SearchOption.AllDirectories));
    }

    [Theory(DisplayName = "S3: entries that escape the output directory are rejected")]
    [Trait("Category", "Contract")]
    [InlineData(@"..\evil.txt")]
    [InlineData("../evil.txt")]
    [InlineData(@"a\..\..\evil.txt")]
    public void ExtractTemplateNormalized_RejectsTraversal(string entryName)
    {
        using var scope = new TestRunScope("extract-traversal");
        var zipPath = Path.Combine(scope.RootPath, "evil.zip");
        CreateZip(zipPath, entryName, "evil");
        var output = scope.CreateDirectory("out");

        Assert.Throws<InvalidDataException>(() => ProjectCreatorService.ExtractTemplateNormalized(zipPath, output));
        Assert.False(File.Exists(Path.Combine(scope.RootPath, "evil.txt")));
    }

    [Fact(DisplayName = "S3: a template without a project folder fails clearly")]
    [Trait("Category", "Integration")]
    public async Task CreateProjectZip_TemplateWithoutProjectFolder_Throws()
    {
        using var scope = new TestRunScope("project-export-flat");
        var contentRoot = scope.CreateDirectory("web-root");
        Directory.CreateDirectory(Path.Combine(contentRoot, "JobTemplate"));
        CreateZip(Path.Combine(contentRoot, "JobTemplate", "BlazorDataOrchestrator.JobCreatorTemplate.zip"), "Program.cs", "// flat");

        var service = new ProjectCreatorService(new TestWebHostEnvironment(contentRoot), NullLogger<ProjectCreatorService>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateProjectZipAsync("Flat", new Dictionary<string, string> { ["main.cs"] = "CS" }));
        Assert.Contains("did not extract to a project folder", error.Message);
    }

    private static void CreateZip(string zipPath, string entryName, string content)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
        writer.Write(content);
    }

    private static void CreateTemplate(string zipPath, string workspace, string csproj = "<Project Sdk=\"Microsoft.NET.Sdk\" />")
    {
        var source = Path.Combine(workspace, "template-source", "BlazorDataOrchestrator.JobCreatorTemplate");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "BlazorDataOrchestrator.JobCreatorTemplate.csproj"), csproj);
        File.WriteAllText(Path.Combine(source, "TemplateIdentity.txt"), "JobCreatorTemplate");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(source)!, zipPath, CompressionLevel.NoCompression, false);
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "BlazorOrchestrator.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}