using BlazorDataOrchestrator.Core;
using BlazorOrchestrator.Testing;
using BlazorOrchestrator.Web.Data;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>
/// The release pipeline (build/Check-Version.ps1), the Upgrade Wizard and BlazorData.net rely on
/// one version. Main's convention: ApplicationVersion.Current and the root SQLVersion file are
/// equal and are bumped on every release; a !SQL script is added only when the schema changes
/// and is named after the version that introduces it, so the newest script may be older than
/// the application version but never newer.
/// </summary>
public class VersionConsistencyTests
{
    private static string SqlFolder => RepoPaths.Src("BlazorOrchestrator.Web", SqlScriptCatalog.FolderName);

    private static IReadOnlyList<string> RepositoryScripts =>
        Directory.EnumerateFiles(SqlFolder, "*.sql").Select(path => Path.GetFileName(path)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    [Fact]
    [Trait("Category", "Version")]
    public void SqlVersionFile_MatchesApplicationVersion()
    {
        var sqlVersion = File.ReadAllText(Path.Combine(RepoPaths.Root, "SQLVersion")).Trim();

        Assert.Equal(ApplicationVersion.Current, sqlVersion);
    }

    [Fact]
    [Trait("Category", "Version")]
    public void ApplicationVersion_UsesTheScriptNameFormat()
    {
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{2}$", ApplicationVersion.Current);
    }

    [Fact]
    [Trait("Category", "Version")]
    public void SqlScriptNames_UseTheVersionFormat()
    {
        Assert.NotEmpty(RepositoryScripts);
        Assert.All(RepositoryScripts, name => Assert.True(SqlScriptCatalog.IsScriptName(name), $"{name} is not named NN.NN.NN.sql"));
    }

    [Fact]
    [Trait("Category", "Version")]
    public void NewestSqlScript_IsNotNewerThanApplicationVersion()
    {
        var newest = RepositoryScripts.Select(SqlScriptCatalog.VersionOf).MaxBy(ApplicationVersion.ToInteger)!;

        Assert.True(ApplicationVersion.ToInteger(newest) <= ApplicationVersion.ToInteger(ApplicationVersion.Current),
            $"!SQL/{newest}.sql is newer than ApplicationVersion.Current ({ApplicationVersion.Current}). Bump ApplicationVersion.Current and SQLVersion.");
    }

    [Fact]
    [Trait("Category", "Version")]
    public void BaselineScript_Exists()
    {
        Assert.Contains(SqlScriptCatalog.BaselineScript, RepositoryScripts);
    }

    [Fact]
    [Trait("Category", "Version")]
    public void BuildRecordsEveryScript_ForTheMissingScriptCheck()
    {
        Assert.Equal(RepositoryScripts, SqlScriptCatalog.ExpectedScripts);
    }

    [Fact]
    [Trait("Category", "Version")]
    public void ReleaseTag_RoundTripsToApplicationVersion()
    {
        var tag = ApplicationVersion.ToReleaseTag(ApplicationVersion.Current);

        Assert.Equal("v1.60.0", ApplicationVersion.ToReleaseTag("01.60.00"));
        Assert.Equal(ApplicationVersion.Current, ApplicationVersion.FromReleaseTag(tag));
    }

    [Theory]
    [Trait("Category", "Version")]
    [InlineData("v1.0.0", "01.00.00")]
    [InlineData("v1.60.0", "01.60.00")]
    [InlineData("v1.2.10", "01.02.10")]
    [InlineData("12.0.1", "12.00.01")]
    public void FromReleaseTag_ConvertsToApplicationVersion(string tag, string expected)
    {
        Assert.Equal(expected, ApplicationVersion.FromReleaseTag(tag));
    }

    [Theory]
    [Trait("Category", "Version")]
    [InlineData("v1.0")]
    [InlineData("v1.0.0-beta")]
    [InlineData("v100.0.0")]
    [InlineData("")]
    public void FromReleaseTag_RejectsOtherFormats(string tag)
    {
        Assert.Throws<FormatException>(() => ApplicationVersion.FromReleaseTag(tag));
    }

    [Theory]
    [Trait("Category", "Version")]
    [InlineData("1.0", 10000)]
    [InlineData("01.00.00", 10000)]
    [InlineData("01.60.00", 16000)]
    [InlineData("01.02.03", 10203)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ToInteger_ReadsOldAndNewFormats(string? version, int expected)
    {
        Assert.Equal(expected, ApplicationVersion.ToInteger(version));
    }
}
