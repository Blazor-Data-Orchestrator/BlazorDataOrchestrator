using BlazorDataOrchestrator.Core;

namespace BlazorOrchestrator.Tests;

/// <summary>
/// The release pipeline, the Upgrade Wizard and BlazorData.net all rely on one version:
/// ApplicationVersion.Current, the root SQLVersion file and the newest !SQL script must agree.
/// </summary>
public class VersionConsistencyTests
{
    [Fact]
    public void SqlVersionFile_MatchesApplicationVersion()
    {
        var sqlVersion = File.ReadAllText(RepositoryPaths.SqlVersionFile).Trim();

        Assert.Equal(ApplicationVersion.Current, sqlVersion);
    }

    [Fact]
    public void NewestSqlScript_MatchesApplicationVersion()
    {
        var newestScript = Directory.EnumerateFiles(RepositoryPaths.SqlScriptFolder, "*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Last();

        Assert.Equal(ApplicationVersion.Current, newestScript);
    }

    [Fact]
    public void SqlScriptNames_UseTheVersionFormat()
    {
        foreach (var script in Directory.EnumerateFiles(RepositoryPaths.SqlScriptFolder, "*.sql"))
        {
            Assert.Matches(@"^\d{2}\.\d{2}\.\d{2}$", Path.GetFileNameWithoutExtension(script));
        }
    }

    [Fact]
    public void ApplicationVersion_UsesTheVersionFormat()
    {
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{2}$", ApplicationVersion.Current);
    }

    [Theory]
    [InlineData("1.0", 10000)]
    [InlineData("01.00.00", 10000)]
    [InlineData("01.02.03", 10203)]
    [InlineData("2.1", 20100)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ToInteger_ReadsOldAndNewFormats(string? version, int expected)
    {
        Assert.Equal(expected, ApplicationVersion.ToInteger(version));
    }

    [Theory]
    [InlineData("v1.0.0", "01.00.00")]
    [InlineData("v1.2.10", "01.02.10")]
    [InlineData("12.0.1", "12.00.01")]
    public void FromReleaseTag_ConvertsToApplicationVersion(string tag, string expected)
    {
        Assert.Equal(expected, ApplicationVersion.FromReleaseTag(tag));
    }

    [Theory]
    [InlineData("v1.0")]
    [InlineData("v1.0.0-beta")]
    [InlineData("v100.0.0")]
    public void FromReleaseTag_RejectsOtherFormats(string tag)
    {
        Assert.Throws<FormatException>(() => ApplicationVersion.FromReleaseTag(tag));
    }
}
