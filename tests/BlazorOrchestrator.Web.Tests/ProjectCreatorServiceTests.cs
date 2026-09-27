using System.IO.Compression;
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

    private static void CreateTemplate(string zipPath, string workspace)
    {
        var source = Path.Combine(workspace, "template-source", "BlazorDataOrchestrator.JobCreatorTemplate");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "BlazorDataOrchestrator.JobCreatorTemplate.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
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