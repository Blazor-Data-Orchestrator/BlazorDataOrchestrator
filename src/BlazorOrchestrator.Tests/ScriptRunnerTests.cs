using BlazorOrchestrator.Web.Data;

namespace BlazorOrchestrator.Tests;

public class ScriptRunnerTests
{
    private static readonly string[] Scripts =
    [
        Path.Combine("x", "02.00.00.sql"),
        Path.Combine("x", "01.00.00.sql"),
        Path.Combine("x", "01.10.00.sql"),
        Path.Combine("x", "01.02.00.sql"),
        Path.Combine("x", "readme.txt")
    ];

    [Fact]
    public void GetPendingScripts_WithoutSchemaVersion_ReturnsAllScriptsInNameOrder()
    {
        var pending = DatabaseInitializer.GetPendingScripts(Scripts, null).Select(Path.GetFileName);

        Assert.Equal(["01.00.00.sql", "01.02.00.sql", "01.10.00.sql", "02.00.00.sql"], pending);
    }

    [Fact]
    public void GetPendingScripts_ReturnsOnlyNewerScripts()
    {
        var pending = DatabaseInitializer.GetPendingScripts(Scripts, "01.02.00").Select(Path.GetFileName);

        Assert.Equal(["01.10.00.sql", "02.00.00.sql"], pending);
    }

    [Fact]
    public void GetPendingScripts_AtNewestVersion_ReturnsNothing()
    {
        Assert.Empty(DatabaseInitializer.GetPendingScripts(Scripts, "02.00.00"));
    }

    [Fact]
    public void GetPendingScripts_ReadsTheOldVersionFormat()
    {
        // "1.0" is the format used before 01.00.00
        var pending = DatabaseInitializer.GetPendingScripts(Scripts, "1.0").Select(Path.GetFileName);

        Assert.Equal(["01.02.00.sql", "01.10.00.sql", "02.00.00.sql"], pending);
    }

    [Fact]
    public void SplitSqlBatches_SplitsOnGoLines()
    {
        var script = "CREATE TABLE A (Id int)\r\nGO\r\n  go  \nSELECT 'GO' AS Word\nGO\nSELECT 1";

        var batches = DatabaseInitializer.SplitSqlBatches(script)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .Select(batch => batch.Trim())
            .ToList();

        Assert.Equal(["CREATE TABLE A (Id int)", "SELECT 'GO' AS Word", "SELECT 1"], batches);
    }

    [Fact]
    public void RepositoryScripts_SplitIntoBatches()
    {
        foreach (var script in Directory.EnumerateFiles(RepositoryPaths.SqlScriptFolder, "*.sql"))
        {
            var batches = DatabaseInitializer.SplitSqlBatches(File.ReadAllText(script))
                .Where(batch => !string.IsNullOrWhiteSpace(batch));

            Assert.NotEmpty(batches);
        }
    }

    [Fact]
    public void InitialScript_CreatesSchemaVersionTable()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryPaths.SqlScriptFolder, "01.00.00.sql"));

        Assert.Contains("CREATE TABLE [dbo].[SchemaVersion]", script);
        Assert.Contains("INSERT INTO [dbo].[SchemaVersion]", script);
    }
}
