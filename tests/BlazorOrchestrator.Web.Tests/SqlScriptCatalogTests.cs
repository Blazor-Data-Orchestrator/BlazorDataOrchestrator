using BlazorOrchestrator.Testing;
using BlazorOrchestrator.Web.Data;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

public class SqlScriptCatalogTests
{
    private static readonly string[] Scripts =
    [
        Path.Combine("x", "02.00.00.sql"),
        Path.Combine("x", "01.00.00.sql"),
        Path.Combine("x", "01.10.00.sql"),
        Path.Combine("x", "01.02.00.sql"),
        Path.Combine("x", "readme.txt"),
        Path.Combine("x", "notes.sql")
    ];

    [Fact]
    [Trait("Category", "Upgrade")]
    public void GetPendingScripts_ReturnsOnlyScriptsNewerThanTheDatabase_InVersionOrder()
    {
        var pending = SqlScriptCatalog.GetPendingScripts(Scripts, "01.00.00", "02.00.00").Select(Path.GetFileName);

        Assert.Equal(["01.02.00.sql", "01.10.00.sql", "02.00.00.sql"], pending);
    }

    [Fact]
    [Trait("Category", "Upgrade")]
    public void GetPendingScripts_StopsAtTheCodeVersion()
    {
        var pending = SqlScriptCatalog.GetPendingScripts(Scripts, "01.00.00", "01.60.00").Select(Path.GetFileName);

        Assert.Equal(["01.02.00.sql", "01.10.00.sql"], pending);
    }

    [Fact]
    [Trait("Category", "Upgrade")]
    public void GetPendingScripts_AtCodeVersion_ReturnsNothing()
    {
        Assert.Empty(SqlScriptCatalog.GetPendingScripts(Scripts, "02.00.00", "02.00.00"));
    }

    [Fact]
    [Trait("Category", "Upgrade")]
    public void GetPendingScripts_ReadsTheOldVersionFormat()
    {
        var pending = SqlScriptCatalog.GetPendingScripts(Scripts, "1.0", "01.10.00").Select(Path.GetFileName);

        Assert.Equal(["01.02.00.sql", "01.10.00.sql"], pending);
    }

    [Fact]
    [Trait("Category", "Upgrade")]
    public void SplitSqlBatches_SplitsOnGoLinesOnly()
    {
        var script = "CREATE TABLE A (Id int)\r\nGO\r\n  go  \nSELECT 'GO' AS Word\nGO\nSELECT 1";

        var batches = SqlScriptCatalog.GetBatches(script).Select(batch => batch.Trim()).ToList();

        Assert.Equal(["CREATE TABLE A (Id int)", "SELECT 'GO' AS Word", "SELECT 1"], batches);
    }

    [Fact]
    [Trait("Category", "Upgrade")]
    public void RepositoryScripts_SplitIntoBatches()
    {
        foreach (var script in Directory.EnumerateFiles(RepoPaths.Src("BlazorOrchestrator.Web", SqlScriptCatalog.FolderName), "*.sql"))
        {
            Assert.NotEmpty(SqlScriptCatalog.GetBatches(File.ReadAllText(script)));
        }
    }

    [Fact(DisplayName = "Review #8: missing scripts are detected before anything runs")]
    [Trait("Category", "Upgrade")]
    public void FindMissing_ReportsScriptsTheBuildShippedWith()
    {
        var directory = Directory.CreateTempSubdirectory("bdo-sql-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "01.00.00.sql"), "SELECT 1");

            Assert.Equal(["01.10.00.sql", "01.20.00.sql"],
                SqlScriptCatalog.FindMissing(directory, ["01.00.00.sql", "01.10.00.sql", "01.20.00.sql"]));
            Assert.Equal(["01.00.00.sql"], SqlScriptCatalog.FindMissing(Path.Combine(directory, "nope"), ["01.00.00.sql"]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Review #8: ApplyPendingScriptsAsync refuses a deployment with missing scripts before touching the database")]
    [Trait("Category", "Upgrade")]
    public async Task ApplyPendingScripts_WithMissingScripts_ThrowsWithoutConnecting()
    {
        var directory = Directory.CreateTempSubdirectory("bdo-sql-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "01.00.00.sql"), "SELECT 1");
            var store = new InMemorySchemaVersionStore("01.00.00");

            // The connection string points nowhere: the check must fail before any connection is opened.
            var ex = await Assert.ThrowsAsync<MissingSqlScriptsException>(() => DatabaseInitializer.ApplyPendingScriptsAsync(
                "Server=tcp:127.0.0.1,1;Database=none;Connect Timeout=1",
                "01.20.00",
                store,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                scriptDirectory: directory,
                expectedScripts: ["01.00.00.sql", "01.20.00.sql"],
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(["01.20.00.sql"], ex.MissingScripts);
            Assert.Equal("01.00.00", store.Version);
            Assert.Equal(0, store.Writes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

internal sealed class InMemorySchemaVersionStore(string? version = null) : ISchemaVersionStore
{
    private readonly object _gate = new();

    public string? Version { get; private set; } = version;
    public int Writes { get; private set; }
    public List<string> History { get; } = new();

    public Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(Version);
    }

    public Task SetAsync(string version, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Version = version;
            Writes++;
            History.Add(version);
        }
        return Task.CompletedTask;
    }
}
