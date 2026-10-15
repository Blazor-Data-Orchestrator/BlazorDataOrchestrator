using Dapper;
using Microsoft.Data.SqlClient;

namespace BlazorOrchestrator.Web.Data;

/// <summary>
/// The schema state recorded in the database itself, written in the same transaction as each
/// script, so a committed script can never be applied twice:
/// <list type="bullet">
/// <item><c>[dbo].[SchemaScriptHistory]</c>: one row per applied (or baseline/backfilled) script.</item>
/// <item><c>[dbo].[SchemaVersionState]</c>: one row with the schema version the database has reached.</item>
/// </list>
/// The <c>SchemaVersion</c> setting in Table Storage (read by the setup gate) is a copy that is
/// synchronized from this state after each run and repaired at startup when it falls behind.
/// The tables are created by code (not a !SQL script) under <see cref="SchemaMigrationLock"/>.
/// </summary>
public static class SchemaCheckpoint
{
    private const string EnsureTablesSql = """
        IF OBJECT_ID(N'[dbo].[SchemaScriptHistory]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[SchemaScriptHistory](
                [ScriptName] [nvarchar](50) NOT NULL CONSTRAINT [PK_SchemaScriptHistory] PRIMARY KEY,
                [Version] [nvarchar](20) NOT NULL,
                [AppliedUtc] [datetime2](7) NOT NULL,
                [Backfilled] [bit] NOT NULL
            )
        END;
        IF OBJECT_ID(N'[dbo].[SchemaVersionState]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[SchemaVersionState](
                [Id] [tinyint] NOT NULL CONSTRAINT [PK_SchemaVersionState] PRIMARY KEY CONSTRAINT [CK_SchemaVersionState_Single] CHECK ([Id] = 1),
                [Version] [nvarchar](20) NOT NULL,
                [UpdatedUtc] [datetime2](7) NOT NULL
            )
        END;
        """;

    public static Task EnsureTablesAsync(SqlConnection connection, SqlTransaction? transaction = null) =>
        connection.ExecuteAsync(EnsureTablesSql, transaction: transaction);

    /// <summary>The version recorded in SQL, or null when the database predates the checkpoint.</summary>
    public static async Task<string?> GetStateVersionAsync(SqlConnection connection, SqlTransaction? transaction = null)
    {
        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(N'[dbo].[SchemaVersionState]', N'U') IS NULL THEN 0 ELSE 1 END", transaction: transaction);
        if (exists == 0) return null;

        return await connection.ExecuteScalarAsync<string?>("SELECT [Version] FROM [dbo].[SchemaVersionState] WHERE [Id] = 1", transaction: transaction);
    }

    public static async Task<HashSet<string>> GetAppliedScriptsAsync(SqlConnection connection, SqlTransaction? transaction = null) =>
        (await connection.QueryAsync<string>("SELECT [ScriptName] FROM [dbo].[SchemaScriptHistory]", transaction: transaction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static Task RecordScriptAsync(SqlConnection connection, SqlTransaction transaction, string scriptName, string version, bool backfilled) =>
        connection.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM [dbo].[SchemaScriptHistory] WHERE [ScriptName] = @scriptName)
                INSERT INTO [dbo].[SchemaScriptHistory] ([ScriptName], [Version], [AppliedUtc], [Backfilled])
                VALUES (@scriptName, @version, SYSUTCDATETIME(), @backfilled)
            """, new { scriptName, version, backfilled }, transaction);

    public static Task SetStateVersionAsync(SqlConnection connection, SqlTransaction transaction, string version) =>
        connection.ExecuteAsync("""
            UPDATE [dbo].[SchemaVersionState] SET [Version] = @version, [UpdatedUtc] = SYSUTCDATETIME() WHERE [Id] = 1;
            IF @@ROWCOUNT = 0
                INSERT INTO [dbo].[SchemaVersionState] ([Id], [Version], [UpdatedUtc]) VALUES (1, @version, SYSUTCDATETIME());
            """, new { version }, transaction);
}
