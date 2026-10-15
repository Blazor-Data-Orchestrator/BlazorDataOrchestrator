using BlazorOrchestrator.Testing;
using BlazorOrchestrator.Web.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>
/// SQL Server integration tests for the schema runner. They run only when BDO_TEST_SQL_SERVER is
/// a server connection string without a database, for example
/// <c>Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true</c>.
/// Each test uses its own database, created and dropped by the test.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SchemaUpgradeSqlTests : IAsyncLifetime
{
    public const string VariableName = "BDO_TEST_SQL_SERVER";

    private readonly string _databaseName = $"bdo_test_{Guid.NewGuid():N}";
    private readonly string _scripts = Directory.CreateTempSubdirectory("bdo-sql-").FullName;
    private string _server = string.Empty;
    private string _database = string.Empty;

    private static readonly string[] Baseline = [SqlScriptCatalog.BaselineScript];

    public ValueTask InitializeAsync()
    {
        _server = Environment.GetEnvironmentVariable(VariableName) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_server))
        {
            _database = new SqlConnectionStringBuilder(_server) { InitialCatalog = _databaseName }.ConnectionString;
        }

        File.Copy(RepoPaths.Src("BlazorOrchestrator.Web", SqlScriptCatalog.FolderName, SqlScriptCatalog.BaselineScript),
            Path.Combine(_scripts, SqlScriptCatalog.BaselineScript));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try { Directory.Delete(_scripts, recursive: true); } catch { }
        if (string.IsNullOrWhiteSpace(_server)) return;

        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_server);
        await connection.ExecuteAsync($"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}] END");
    }

    private void RequireSqlServer() =>
        Assert.SkipWhen(string.IsNullOrWhiteSpace(_server), $"Set {VariableName} to run SQL Server integration tests.");

    private Task<bool> InitializeAsync(InMemorySchemaVersionStore store) =>
        DatabaseInitializer.InitializeDatabaseAsync(_database, NullLogger.Instance, versionStore: store, currentVersion: "01.60.00",
            scriptDirectory: _scripts, cancellationToken: TestContext.Current.CancellationToken);

    private Task<SchemaUpgradeResult> UpgradeAsync(InMemorySchemaVersionStore store, string codeVersion = "01.60.00") =>
        DatabaseInitializer.ApplyPendingScriptsAsync(_database, codeVersion, store, NullLogger.Instance,
            scriptDirectory: _scripts,
            expectedScripts: Directory.EnumerateFiles(_scripts, "*.sql").Select(path => Path.GetFileName(path)).ToList(),
            cancellationToken: TestContext.Current.CancellationToken);

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(_database);
        return await connection.ExecuteScalarAsync<T>(sql);
    }

    private void AddScript(string name, string body) => File.WriteAllText(Path.Combine(_scripts, name), body);

    [Fact]
    public async Task NewDatabase_IsCreatedFromTheBaseline_AndRecordsTheCodeVersion()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();

        Assert.True(await InitializeAsync(store));

        Assert.Equal("01.60.00", store.Version);
        Assert.Equal(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL THEN 0 ELSE 1 END"));
    }

    [Fact(DisplayName = "Review #3: startup / the anonymous installer never change an existing schema")]
    public async Task ExistingSchema_IsNotChangedByInitialize()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        await store.SetAsync("01.00.00", TestContext.Current.CancellationToken);
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[FromUpgrade] ([Id] int)\r\nGO\r\n");
        await using (var connection = new SqlConnection(_database))
        {
            await connection.ExecuteAsync("DROP TABLE [dbo].[AspNetUserTokens]");
        }

        Assert.False(await InitializeAsync(store));

        Assert.Equal("01.00.00", store.Version);
        Assert.Equal(0, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'[dbo].[FromUpgrade]', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'[dbo].[AspNetUserTokens]', N'U') IS NULL THEN 0 ELSE 1 END"));
    }

    [Fact]
    public async Task ExistingSchema_WithoutRecordedVersion_IsRecordedAsTheBaseline()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        var empty = new InMemorySchemaVersionStore();

        Assert.False(await InitializeAsync(empty));

        Assert.Equal(SqlScriptCatalog.BaselineVersion, empty.Version);
    }

    [Fact]
    public async Task NewerScripts_AreAppliedInVersionOrder_AndRecorded()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        await store.SetAsync("01.00.00", TestContext.Current.CancellationToken);
        store.History.Clear();
        AddScript("01.20.00.sql", "INSERT INTO [dbo].[UpgradeLog] VALUES (N'01.20.00')\r\nGO\r\n");
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[UpgradeLog] ([Script] nvarchar(20) NOT NULL)\r\nGO\r\nINSERT INTO [dbo].[UpgradeLog] VALUES (N'01.10.00')\r\nGO\r\n");

        var result = await UpgradeAsync(store);

        Assert.Equal(["01.10.00.sql", "01.20.00.sql"], result.AppliedScripts);
        Assert.Equal(["01.10.00", "01.20.00", "01.60.00"], store.History);
        await using var connection = new SqlConnection(_database);
        Assert.Equal(["01.10.00", "01.20.00"], (await connection.QueryAsync<string>("SELECT [Script] FROM [dbo].[UpgradeLog]")).ToList());

        // Up to date: nothing runs again.
        var again = await UpgradeAsync(store);
        Assert.Empty(again.AppliedScripts);
    }

    [Fact]
    public async Task FailingScript_IsRolledBack_AndKeepsThePreviousVersion()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        await store.SetAsync("01.00.00", TestContext.Current.CancellationToken);
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[Partial] ([Id] int)\r\nGO\r\nSELECT * FROM [dbo].[DoesNotExist]\r\nGO\r\n");

        var ex = await Assert.ThrowsAsync<SchemaScriptFailedException>(() => UpgradeAsync(store));

        Assert.Equal("01.10.00.sql", ex.Script);
        Assert.Equal("01.00.00", store.Version);
        Assert.Equal(0, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'[dbo].[Partial]', N'U') IS NULL THEN 0 ELSE 1 END"));
    }

    [Fact(DisplayName = "Review #4: two concurrent upgrades are serialized by the database lock and apply each script once")]
    public async Task ConcurrentUpgrades_ApplyEachScriptOnce()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        await store.SetAsync("01.00.00", TestContext.Current.CancellationToken);
        await using (var connection = new SqlConnection(_database))
        {
            await connection.ExecuteAsync("CREATE TABLE [dbo].[UpgradeLog] ([Script] nvarchar(20) NOT NULL)");
        }
        // Not idempotent on purpose, and slow, so an unserialized second run would insert again.
        AddScript("01.10.00.sql", "WAITFOR DELAY '00:00:02'\r\nGO\r\nINSERT INTO [dbo].[UpgradeLog] VALUES (N'01.10.00')\r\nGO\r\n");
        AddScript("01.20.00.sql", "INSERT INTO [dbo].[UpgradeLog] VALUES (N'01.20.00')\r\nGO\r\n");

        var results = await Task.WhenAll(Task.Run(() => UpgradeAsync(store)), Task.Run(() => UpgradeAsync(store)));

        Assert.Single(results, r => r.AppliedScripts.Count == 2);
        Assert.Single(results, r => r.AppliedScripts.Count == 0);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[UpgradeLog]"));
        Assert.Equal("01.60.00", store.Version);
    }

    [Fact(DisplayName = "Review #4: concurrent initializers (two replicas starting) create the schema once")]
    public async Task ConcurrentInitializers_CreateTheSchemaOnce()
    {
        RequireSqlServer();
        // Create the empty database first so both initializers race on the schema, not on CREATE DATABASE.
        await using (var connection = new SqlConnection(_server))
        {
            await connection.ExecuteAsync($"CREATE DATABASE [{_databaseName}]");
        }
        var store = new InMemorySchemaVersionStore();

        var created = await Task.WhenAll(Task.Run(() => InitializeAsync(store)), Task.Run(() => InitializeAsync(store)));

        Assert.Single(created, c => c);
        Assert.Equal("01.60.00", store.Version);
    }

    [Fact(DisplayName = "Review #8: a deployment that lost a script changes nothing")]
    public async Task MissingScript_ChangesNothing()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        await store.SetAsync("01.00.00", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<MissingSqlScriptsException>(() =>
            DatabaseInitializer.ApplyPendingScriptsAsync(_database, "01.60.00", store, NullLogger.Instance,
                scriptDirectory: _scripts, expectedScripts: ["01.00.00.sql", "01.20.00.sql"],
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("01.00.00", store.Version);
    }
}
