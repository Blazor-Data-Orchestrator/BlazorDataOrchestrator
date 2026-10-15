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
/// Each test uses its own database, created and dropped by the test. Unless a test says
/// otherwise the database is installed at 01.00.00 and upgraded to 01.60.00.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SchemaUpgradeSqlTests : IAsyncLifetime
{
    public const string VariableName = "BDO_TEST_SQL_SERVER";
    private const string InstalledVersion = "01.00.00";
    private const string CodeVersion = "01.60.00";

    private readonly string _databaseName = $"bdo_test_{Guid.NewGuid():N}";
    private readonly string _scripts = Directory.CreateTempSubdirectory("bdo-sql-").FullName;
    private string _server = string.Empty;
    private string _database = string.Empty;

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

    private Task<bool> InitializeAsync(InMemorySchemaVersionStore store, string version = InstalledVersion) =>
        DatabaseInitializer.InitializeDatabaseAsync(_database, NullLogger.Instance, versionStore: store, currentVersion: version,
            scriptDirectory: _scripts, cancellationToken: TestContext.Current.CancellationToken);

    private Task<SchemaUpgradeResult> UpgradeAsync(InMemorySchemaVersionStore store, string codeVersion = CodeVersion) =>
        DatabaseInitializer.ApplyPendingScriptsAsync(_database, codeVersion, store, NullLogger.Instance,
            scriptDirectory: _scripts,
            expectedScripts: Directory.EnumerateFiles(_scripts, "*.sql").Select(path => Path.GetFileName(path)).ToList(),
            cancellationToken: TestContext.Current.CancellationToken);

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(_database);
        return await connection.ExecuteScalarAsync<T>(sql);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_database);
        await connection.ExecuteAsync(sql);
    }

    private Task<int> TableExistsAsync(string table) =>
        ScalarAsync<int>($"SELECT CASE WHEN OBJECT_ID(N'{table}', N'U') IS NULL THEN 0 ELSE 1 END");

    private async Task<List<string>> HistoryAsync()
    {
        await using var connection = new SqlConnection(_database);
        return (await connection.QueryAsync<string>("SELECT [ScriptName] FROM [dbo].[SchemaScriptHistory] ORDER BY [ScriptName]")).ToList();
    }

    private Task<string?> SqlVersionAsync() => ScalarAsync<string?>("SELECT [Version] FROM [dbo].[SchemaVersionState] WHERE [Id] = 1");

    /// <summary>Makes the database look like one created before the SQL checkpoint existed.</summary>
    private Task DropCheckpointAsync() =>
        ExecuteAsync("DROP TABLE [dbo].[SchemaScriptHistory]; DROP TABLE [dbo].[SchemaVersionState];");

    private void AddScript(string name, string body) => File.WriteAllText(Path.Combine(_scripts, name), body);

    private void AddUpgradeLogScripts()
    {
        AddScript("01.20.00.sql", "INSERT INTO [dbo].[UpgradeLog] VALUES (N'01.20.00')\r\nGO\r\n");
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[UpgradeLog] ([Script] nvarchar(20) NOT NULL)\r\nGO\r\nINSERT INTO [dbo].[UpgradeLog] VALUES (N'01.10.00')\r\nGO\r\n");
    }

    [Fact]
    public async Task NewDatabase_IsCreatedFromTheBaseline_AndRecordsTheCodeVersion()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();

        Assert.True(await InitializeAsync(store, CodeVersion));

        Assert.Equal(CodeVersion, store.Version);
        Assert.Equal(1, await TableExistsAsync("[dbo].[AspNetUsers]"));
        Assert.Equal(CodeVersion, await SqlVersionAsync());
        Assert.Equal(["01.00.00.sql"], await HistoryAsync());
    }

    [Fact(DisplayName = "Review #3: startup / the anonymous installer never change an existing schema")]
    public async Task ExistingSchema_IsNotChangedByInitialize()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[FromUpgrade] ([Id] int)\r\nGO\r\n");
        await ExecuteAsync("DROP TABLE [dbo].[AspNetUserTokens]");

        Assert.False(await InitializeAsync(store, CodeVersion));

        Assert.Equal(InstalledVersion, store.Version);
        Assert.Equal(InstalledVersion, await SqlVersionAsync());
        Assert.Equal(0, await TableExistsAsync("[dbo].[FromUpgrade]"));
        Assert.Equal(0, await TableExistsAsync("[dbo].[AspNetUserTokens]"));
    }

    [Fact]
    public async Task PreCheckpointSchema_WithoutRecordedVersion_IsRecordedAsTheBaseline()
    {
        RequireSqlServer();
        await InitializeAsync(new InMemorySchemaVersionStore());
        await DropCheckpointAsync();
        var empty = new InMemorySchemaVersionStore();

        Assert.False(await InitializeAsync(empty, CodeVersion));

        Assert.Equal(SqlScriptCatalog.BaselineVersion, empty.Version);
    }

    [Fact]
    public async Task NewerScripts_AreAppliedInVersionOrder_AndRecordedInSql()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        store.History.Clear();
        AddUpgradeLogScripts();

        var result = await UpgradeAsync(store);

        Assert.Equal(["01.10.00.sql", "01.20.00.sql"], result.AppliedScripts);
        Assert.Equal([CodeVersion], store.History);
        Assert.Equal(CodeVersion, await SqlVersionAsync());
        Assert.Equal(["01.00.00.sql", "01.10.00.sql", "01.20.00.sql"], await HistoryAsync());
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
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[Partial] ([Id] int)\r\nGO\r\nSELECT * FROM [dbo].[DoesNotExist]\r\nGO\r\n");

        var ex = await Assert.ThrowsAsync<SchemaScriptFailedException>(() => UpgradeAsync(store));

        Assert.Equal("01.10.00.sql", ex.Script);
        Assert.Equal(InstalledVersion, store.Version);
        Assert.Equal(InstalledVersion, await SqlVersionAsync());
        Assert.DoesNotContain("01.10.00.sql", await HistoryAsync());
        Assert.Equal(0, await TableExistsAsync("[dbo].[Partial]"));
    }

    [Fact(DisplayName = "PR #47 HIGH-1: a failed SchemaVersion write after a script commits never replays the script")]
    public async Task VersionStoreWriteFails_RetryDoesNotReplayCommittedScripts()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        AddUpgradeLogScripts();
        store.FailNextSets = 1;

        // Both scripts commit; only the copy to the SchemaVersion setting fails.
        var failure = await Assert.ThrowsAsync<SchemaVersionSyncException>(() => UpgradeAsync(store));
        Assert.Equal(CodeVersion, failure.Version);
        Assert.Equal(InstalledVersion, store.Version);
        Assert.Equal(CodeVersion, await SqlVersionAsync());

        // Retry Upgrade (or another replica): nothing is re-run, the setting is repaired.
        var retry = await UpgradeAsync(store);

        Assert.Empty(retry.AppliedScripts);
        Assert.Equal(CodeVersion, store.Version);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[UpgradeLog]"));
    }

    [Fact(DisplayName = "PR #47 HIGH-1: startup repairs a SchemaVersion setting that fell behind the SQL checkpoint, without applying anything")]
    public async Task Startup_RepairsTheSettingFromTheSqlCheckpoint()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        AddUpgradeLogScripts();
        store.FailNextSets = 1;
        await Assert.ThrowsAsync<SchemaVersionSyncException>(() => UpgradeAsync(store));
        Assert.Equal(InstalledVersion, store.Version);

        Assert.False(await InitializeAsync(store, CodeVersion));

        Assert.Equal(CodeVersion, store.Version);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[UpgradeLog]"));
    }

    [Fact(DisplayName = "PR #47 HIGH-1: a database that predates the checkpoint is backfilled once from SchemaVersion")]
    public async Task PreCheckpointDatabase_IsBackfilledFromTheSetting()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        // 01.10.00 was applied by an earlier (pre-checkpoint) wizard; it is not idempotent.
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[OnlyOnce] ([Id] int)\r\nGO\r\n");
        await ExecuteAsync("CREATE TABLE [dbo].[OnlyOnce] ([Id] int)");
        AddScript("01.20.00.sql", "CREATE TABLE [dbo].[NewIn120] ([Id] int)\r\nGO\r\n");
        await DropCheckpointAsync();
        await store.SetAsync("01.10.00", TestContext.Current.CancellationToken);

        var result = await UpgradeAsync(store);

        Assert.Equal(["01.20.00.sql"], result.AppliedScripts);
        Assert.Equal(["01.00.00.sql", "01.10.00.sql", "01.20.00.sql"], await HistoryAsync());
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[SchemaScriptHistory] WHERE [Backfilled] = 1"));
        Assert.Equal(CodeVersion, await SqlVersionAsync());
        Assert.Equal(CodeVersion, store.Version);
    }

    [Fact(DisplayName = "PR #47 HIGH-1: the backfill fails closed when the SchemaVersion setting cannot be read")]
    public async Task Backfill_WhenTheSettingCannotBeRead_ChangesNothing()
    {
        RequireSqlServer();
        await InitializeAsync(new InMemorySchemaVersionStore());
        AddScript("01.10.00.sql", "CREATE TABLE [dbo].[NotYet] ([Id] int)\r\nGO\r\n");
        await DropCheckpointAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseInitializer.ApplyPendingScriptsAsync(_database, CodeVersion, new UnreadableStore(), NullLogger.Instance,
                scriptDirectory: _scripts, expectedScripts: ["01.00.00.sql", "01.10.00.sql"],
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await TableExistsAsync("[dbo].[NotYet]"));
        Assert.Null(await SqlVersionAsync());
    }

    [Fact(DisplayName = "Review #4: two concurrent upgrades are serialized by the database lock and apply each script once")]
    public async Task ConcurrentUpgrades_ApplyEachScriptOnce()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);
        await ExecuteAsync("CREATE TABLE [dbo].[UpgradeLog] ([Script] nvarchar(20) NOT NULL)");
        // Not idempotent on purpose, and slow, so an unserialized second run would insert again.
        AddScript("01.10.00.sql", "WAITFOR DELAY '00:00:02'\r\nGO\r\nINSERT INTO [dbo].[UpgradeLog] VALUES (N'01.10.00')\r\nGO\r\n");
        AddScript("01.20.00.sql", "INSERT INTO [dbo].[UpgradeLog] VALUES (N'01.20.00')\r\nGO\r\n");

        var results = await Task.WhenAll(Task.Run(() => UpgradeAsync(store)), Task.Run(() => UpgradeAsync(store)));

        Assert.Single(results, r => r.AppliedScripts.Count == 2);
        Assert.Single(results, r => r.AppliedScripts.Count == 0);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[UpgradeLog]"));
        Assert.Equal(CodeVersion, store.Version);
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

        var created = await Task.WhenAll(Task.Run(() => InitializeAsync(store, CodeVersion)), Task.Run(() => InitializeAsync(store, CodeVersion)));

        Assert.Single(created, c => c);
        Assert.Equal(CodeVersion, store.Version);
    }

    [Fact(DisplayName = "Review #8: a deployment that lost a script changes nothing")]
    public async Task MissingScript_ChangesNothing()
    {
        RequireSqlServer();
        var store = new InMemorySchemaVersionStore();
        await InitializeAsync(store);

        await Assert.ThrowsAsync<MissingSqlScriptsException>(() =>
            DatabaseInitializer.ApplyPendingScriptsAsync(_database, CodeVersion, store, NullLogger.Instance,
                scriptDirectory: _scripts, expectedScripts: ["01.00.00.sql", "01.20.00.sql"],
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(InstalledVersion, store.Version);
        Assert.Equal(InstalledVersion, await SqlVersionAsync());
    }

    private sealed class UnreadableStore : ISchemaVersionStore
    {
        public Task<string?> GetAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated Table Storage outage.");

        public Task SetAsync(string version, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated Table Storage outage.");
    }
}
