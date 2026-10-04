using System.IO.Compression;
using System.Text.Json.Nodes;
using BlazorDataOrchestrator.Core.Configuration;
using BlazorOrchestrator.Testing;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

public class TemplateZipTests
{
    private const string EntryRoot = "BlazorDataOrchestrator.JobCreatorTemplate/";
    private static readonly string[] ExcludedDirectories = { "bin", "obj", "Properties", ".vs", "__pycache__" };
    private static readonly string[] ExcludedFiles = { "execution_errors.log", "resolved.appsettings.json", "runner.py" };

    private static string TemplateSource => RepoPaths.Src("BlazorDataOrchestrator.JobCreatorTemplate");

    private static string TemplateZip => RepoPaths.Src("BlazorOrchestrator.Web", "JobTemplate", "BlazorDataOrchestrator.JobCreatorTemplate.zip");

    [Fact(DisplayName = "S2: the built template zip matches the template source")]
    [Trait("Category", "Contract")]
    public void TemplateZip_MatchesTemplateSource()
    {
        using var archive = ZipFile.OpenRead(TemplateZip);
        var entries = archive.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);

        var checkedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(TemplateSource, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(TemplateSource, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments[..^1].Any(s => ExcludedDirectories.Contains(s, StringComparer.OrdinalIgnoreCase)) ||
                ExcludedFiles.Contains(segments[^1], StringComparer.OrdinalIgnoreCase) ||
                segments[^1].EndsWith(".csproj.user", StringComparison.OrdinalIgnoreCase) ||
                segments[^1].EndsWith(".suo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.True(entries.TryGetValue(EntryRoot + relative, out var entry), $"Template zip is stale: '{relative}' is missing.");

            // The script patches the csproj ProjectReference for the extracted layout.
            if (relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = entry!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            Assert.True(File.ReadAllBytes(file).AsSpan().SequenceEqual(buffer.ToArray()), $"Template zip is stale: '{relative}' differs from source.");
            checkedFiles++;
        }

        Assert.True(checkedFiles > 20, $"Only {checkedFiles} files were compared.");
    }

    [Fact(DisplayName = "S2: the template zip carries the Saving overlay fix (d7b13ea)")]
    [Trait("Category", "Contract")]
    public void TemplateZip_ContainsSavingOverlayFix()
    {
        using var archive = ZipFile.OpenRead(TemplateZip);
        var home = archive.GetEntry(EntryRoot + "Components/Pages/Home.razor");
        Assert.NotNull(home);
        using var reader = new StreamReader(home!.Open());
        Assert.Contains("HideSavingOverlayAsync", reader.ReadToEnd());
    }

    [Fact(DisplayName = "S3: the template zip uses forward-slash entry names")]
    [Trait("Category", "Contract")]
    public void TemplateZip_HasNoBackslashEntries()
    {
        using var archive = ZipFile.OpenRead(TemplateZip);
        Assert.NotEmpty(archive.Entries);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains('\\'));
        Assert.Contains(archive.Entries, e => e.FullName == EntryRoot + "BlazorDataOrchestrator.JobCreatorTemplate.csproj");
    }

    [Theory(DisplayName = "The template zip stages the current SKILL.md files for the AI chat")]
    [Trait("Category", "Contract")]
    [InlineData("coding-a-job-csharp")]
    [InlineData("coding-a-job-python")]
    public void TemplateZip_ContainsCurrentSkillFiles(string skill)
    {
        using var archive = ZipFile.OpenRead(TemplateZip);
        var entry = archive.GetEntry($"{EntryRoot}Skills/{skill}/SKILL.md");
        Assert.NotNull(entry);

        using var stream = entry!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var onDisk = File.ReadAllBytes(Path.Combine(RepoPaths.Root, ".github", "skills", skill, "SKILL.md"));
        Assert.True(onDisk.AsSpan().SequenceEqual(buffer.ToArray()), $"Template zip is stale: Skills/{skill}/SKILL.md differs from .github/skills.");
    }

    [Fact(DisplayName = "The template zip has no stale *.instructions.md copies")]
    [Trait("Category", "Contract")]
    public void TemplateZip_ContainsNoInstructionsMd()
    {
        using var archive = ZipFile.OpenRead(TemplateZip);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.EndsWith(".instructions.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "S5: the template's Development settings use the dedicated Azurite ports")]
    [Trait("Category", "Contract")]
    public void TemplateDevSettings_UseDedicatedAzuritePorts()
    {
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(TemplateSource, "appsettings.Development.json")))!;
        var connectionStrings = settings["ConnectionStrings"]!;

        Assert.Equal(LocalDevEndpoints.BlobConnectionString, connectionStrings["blobs"]!.GetValue<string>());
        Assert.Equal(LocalDevEndpoints.QueueConnectionString, connectionStrings["queues"]!.GetValue<string>());
        Assert.Equal(LocalDevEndpoints.TableConnectionString, connectionStrings["tables"]!.GetValue<string>());
        Assert.Contains($"127.0.0.1,{LocalDevEndpoints.SqlPort}", connectionStrings["blazororchestratordb"]!.GetValue<string>());
        Assert.Equal((10100, 10101, 10102), (LocalDevEndpoints.BlobPort, LocalDevEndpoints.QueuePort, LocalDevEndpoints.TablePort));
    }

    [Fact(DisplayName = "S5: the AppHost pins Azurite with the shared constants")]
    [Trait("Category", "Contract")]
    public void AppHost_UsesSharedAzuritePorts()
    {
        var appHost = File.ReadAllText(RepoPaths.Src("BlazorOrchestrator.AppHost", "Program.cs"));
        Assert.Contains("WithBlobPort(LocalDevEndpoints.BlobPort)", appHost);
        Assert.Contains("WithQueuePort(LocalDevEndpoints.QueuePort)", appHost);
        Assert.Contains("WithTablePort(LocalDevEndpoints.TablePort)", appHost);
    }
}
