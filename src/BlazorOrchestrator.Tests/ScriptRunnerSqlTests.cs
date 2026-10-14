using BlazorOrchestrator.Web.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorOrchestrator.Tests;

/// <summary>
/// Runs only when BDO_TEST_SQL_SERVER is set to a SQL Server connection string without a database,
/// for example: Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true
/// </summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public const string VariableName = "BDO_TEST_SQL_SERVER";

    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableName)))
        {
            Skip = $"Set {VariableName} to run SQL Server integration tests.";
        }
    }
}

public sealed class ScriptRunnerSqlTests : IAsyncLifetime
{
    private readonly string _databaseName = $"bdo_test_{Guid.NewGuid():N}";
    private readonly string _scriptDirectory = Path.Combine(Path.GetTempPath(), $"bdo-sql-{Guid.NewGuid():N}");
    private string _serverConnectionString = string.Empty;
    private string _databaseConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(SqlFactAttribute.VariableName);
        if (string.IsNullOrWhiteSpace(server)) return;

        _serverConnectionString = server;
        _databaseConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _databaseName }.ConnectionString;

        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.ExecuteAsync($"CREATE DATABASE [{_databaseName}]");

        Directory.CreateDirectory(_scriptDirectory);
        File.Copy(Path.Combine(RepositoryPaths.SqlScriptFolder, "01.00.00.sql"), Path.Combine(_scriptDirectory, "01.00.00.sql"));
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_scriptDirectory)) Directory.Delete(_scriptDirectory, recursive: true);
        if (string.IsNullOrEmpty(_serverConnectionString)) return;

        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.ExecuteAsync($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
    }

    [SqlFact]
    public async Task NewDatabase_GetsEveryScriptAndSchemaVersion()
    {
        var applied = await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);

        Assert.Equal(["01.00.00.sql"], applied);
        Assert.Equal("01.00.00", await DatabaseInitializer.GetSchemaVersionAsync(_databaseConnectionString));

        await using var connection = new SqlConnection(_databaseConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[SchemaVersion]"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'ADMINISTRATOR'"));
    }

    [SqlFact]
    public async Task UpToDateDatabase_RunsNothing()
    {
        await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);

        var applied = await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);

        Assert.Empty(applied);
    }

    [SqlFact]
    public async Task NewerScripts_AreAppliedInOrder()
    {
        await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);

        File.WriteAllText(Path.Combine(_scriptDirectory, "01.02.00.sql"), "INSERT INTO [dbo].[UpgradeLog] VALUES (N'01.02.00')\r\nGO\r\n");
        File.WriteAllText(Path.Combine(_scriptDirectory, "01.01.00.sql"), "CREATE TABLE [dbo].[UpgradeLog] ([Script] nvarchar(20) NOT NULL)\r\nGO\r\nINSERT INTO [dbo].[UpgradeLog] VALUES (N'01.01.00')\r\nGO\r\n");

        var applied = await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);

        Assert.Equal(["01.01.00.sql", "01.02.00.sql"], applied);
        Assert.Equal("01.02.00", await DatabaseInitializer.GetSchemaVersionAsync(_databaseConnectionString));

        await using var connection = new SqlConnection(_databaseConnectionString);
        var log = (await connection.QueryAsync<string>("SELECT [Script] FROM [dbo].[UpgradeLog]")).ToList();
        Assert.Equal(["01.01.00", "01.02.00"], log);
    }

    [SqlFact]
    public async Task FailingScript_IsRolledBackAndKeepsThePreviousVersion()
    {
        await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);

        File.WriteAllText(Path.Combine(_scriptDirectory, "01.01.00.sql"), "CREATE TABLE [dbo].[Partial] ([Id] int)\r\nGO\r\nSELECT * FROM [dbo].[DoesNotExist]\r\nGO\r\n");

        await Assert.ThrowsAsync<SqlException>(() =>
            DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory));

        Assert.Equal("01.00.00", await DatabaseInitializer.GetSchemaVersionAsync(_databaseConnectionString));

        await using var connection = new SqlConnection(_databaseConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'[dbo].[Partial]', N'U') IS NULL THEN 0 ELSE 1 END"));
    }

    [SqlFact]
    public async Task DatabaseWithoutSchemaVersionTable_IsUpgraded()
    {
        // A database created by the old initializer: tables exist but there is no SchemaVersion table
        await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);
        await using (var connection = new SqlConnection(_databaseConnectionString))
        {
            await connection.ExecuteAsync("DROP TABLE [dbo].[SchemaVersion]");
        }

        Assert.Null(await DatabaseInitializer.GetSchemaVersionAsync(_databaseConnectionString));

        var applied = await DatabaseInitializer.ApplyPendingScriptsAsync(_databaseConnectionString, NullLogger.Instance, scriptDirectory: _scriptDirectory);

        Assert.Equal(["01.00.00.sql"], applied);
        Assert.Equal("01.00.00", await DatabaseInitializer.GetSchemaVersionAsync(_databaseConnectionString));
    }
}
