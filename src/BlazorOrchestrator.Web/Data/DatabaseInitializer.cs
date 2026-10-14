using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using BlazorDataOrchestrator.Core;

namespace BlazorOrchestrator.Web.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Folder (under the application base directory) that holds the versioned schema scripts.
    /// Each script is named after the version it brings the database to, e.g. 01.00.00.sql.
    /// </summary>
    public const string ScriptFolderName = "!SQL";

    private const int ScriptCommandTimeoutSeconds = 300;

    /// <summary>
    /// Creates the database when it is missing and applies every !SQL script newer than the
    /// version recorded in [dbo].[SchemaVersion].
    /// </summary>
    /// <param name="initialInstallOnly">
    /// When true, scripts are only applied to a database that has no BlazorOrchestrator schema yet.
    /// An existing database is left for the Upgrade Wizard (schema upgrades are never automatic).
    /// </param>
    public static async Task EnsureDatabaseAsync(IConfiguration configuration, ILogger logger, Action<string>? onProgress = null, bool initialInstallOnly = false)
    {
        var connStr = configuration.GetConnectionString("blazororchestratordb");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            var msg = "No blazororchestratordb connection string found; skipping database initialization.";
            logger.LogWarning(msg);
            onProgress?.Invoke($"⚠️ {msg}");
            return;
        }

        try
        {
            onProgress?.Invoke("📋 Parsing connection string...");

            // First, ensure the database exists
            var builder = new SqlConnectionStringBuilder(connStr);
            var databaseName = builder.InitialCatalog;

            if (string.IsNullOrEmpty(databaseName))
            {
                var msg = "Connection string does not contain a database name (Initial Catalog).";
                logger.LogError(msg);
                onProgress?.Invoke($"❌ {msg}");
                throw new InvalidOperationException(msg);
            }

            onProgress?.Invoke($"🔍 Checking if database '{databaseName}' exists...");

            // Try to connect to master to check/create database (works for local SQL Server).
            // For Azure SQL, the provisioned user may not have master access,
            // so we skip database creation (Aspire provisions it automatically).
            try
            {
                builder.InitialCatalog = "master";
                var masterConnStr = builder.ConnectionString;

                await using (var masterConnection = new SqlConnection(masterConnStr))
                {
                    await masterConnection.OpenAsync();
                    onProgress?.Invoke("✅ Connected to SQL Server (master database).");

                    // Check if database exists
                    var dbExistsQuery = "SELECT database_id FROM sys.databases WHERE name = @dbName";
                    var dbExists = await masterConnection.ExecuteScalarAsync<int?>(dbExistsQuery, new { dbName = databaseName });

                    if (dbExists == null)
                    {
                        onProgress?.Invoke($"📦 Creating database '{databaseName}'...");
                        await masterConnection.ExecuteAsync($"CREATE DATABASE [{databaseName}]");
                        logger.LogInformation("Database '{DatabaseName}' created successfully.", databaseName);
                        onProgress?.Invoke($"✅ Database '{databaseName}' created successfully!");
                    }
                    else
                    {
                        onProgress?.Invoke($"✅ Database '{databaseName}' already exists.");
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 18456 || ex.Number == 916)
            {
                // Login failed for master (Azure SQL restricts master access for provisioned users).
                // The database should already exist (provisioned by Aspire/azd), so continue.
                logger.LogInformation("Cannot access master database (Azure SQL). Assuming database '{DatabaseName}' is already provisioned.", databaseName);
                onProgress?.Invoke($"ℹ️ Skipping database creation (Azure SQL - database is pre-provisioned).");
            }

            if (initialInstallOnly && await HasExistingSchemaAsync(connStr))
            {
                var msg = "Database schema already exists; newer scripts are applied by the Upgrade Wizard.";
                logger.LogInformation(msg);
                onProgress?.Invoke($"ℹ️ {msg}");
                return;
            }

            await ApplyPendingScriptsAsync(connStr, logger, onProgress);

            onProgress?.Invoke("🎉 Database setup complete!");
        }
        catch (Exception ex)
        {
            var errorMsg = $"Failed to initialize database: {ex.Message}";
            logger.LogError(ex, "Failed to initialize database schema.");
            onProgress?.Invoke($"❌ {errorMsg}");
            throw;
        }
    }

    /// <summary>
    /// Applies every !SQL script newer than [dbo].[SchemaVersion].[Version], in name order.
    /// Each script runs in its own transaction (split on GO) together with the SchemaVersion
    /// update, so a failed script leaves the database at the previous version.
    /// </summary>
    /// <returns>The names of the scripts that were applied.</returns>
    public static async Task<IReadOnlyList<string>> ApplyPendingScriptsAsync(
        string connectionString,
        ILogger logger,
        Action<string>? onProgress = null,
        string? scriptDirectory = null,
        CancellationToken cancellationToken = default)
    {
        scriptDirectory ??= GetScriptDirectory();

        onProgress?.Invoke("🔌 Connecting to database...");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        onProgress?.Invoke($"✅ Connected to database '{connection.Database}'.");

        var currentVersion = await GetSchemaVersionAsync(connection, cancellationToken);
        onProgress?.Invoke($"🔢 Current schema version: {currentVersion ?? "(none)"}");

        var pendingScripts = GetPendingScripts(Directory.EnumerateFiles(scriptDirectory, "*.sql"), currentVersion);
        if (pendingScripts.Count == 0)
        {
            var upToDate = "Database schema is up to date; no scripts to run.";
            logger.LogInformation(upToDate);
            onProgress?.Invoke($"✅ {upToDate}");
            return Array.Empty<string>();
        }

        onProgress?.Invoke($"📄 {pendingScripts.Count} script(s) to run: {string.Join(", ", pendingScripts.Select(Path.GetFileName))}");

        var applied = new List<string>();
        foreach (var scriptPath in pendingScripts)
        {
            var scriptName = Path.GetFileName(scriptPath);
            var scriptVersion = Path.GetFileNameWithoutExtension(scriptPath);
            var batches = SplitSqlBatches(await File.ReadAllTextAsync(scriptPath, cancellationToken))
                .Where(batch => !string.IsNullOrWhiteSpace(batch))
                .ToList();

            onProgress?.Invoke($"⚡ Running {scriptName} ({batches.Count} batches)...");

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var batch in batches)
                {
                    await connection.ExecuteAsync(new CommandDefinition(batch, transaction: transaction,
                        commandTimeout: ScriptCommandTimeoutSeconds, cancellationToken: cancellationToken));
                }

                await SetSchemaVersionAsync(connection, transaction, scriptVersion, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                logger.LogError(ex, "Schema script {ScriptName} failed and was rolled back.", scriptName);
                onProgress?.Invoke($"❌ {scriptName} failed and was rolled back: {ex.Message}");
                throw;
            }

            applied.Add(scriptName);
            logger.LogInformation("Applied schema script {ScriptName}.", scriptName);
            onProgress?.Invoke($"✅ {scriptName} applied. Schema version is now {scriptVersion}.");
        }

        return applied;
    }

    /// <summary>
    /// Reads the schema version recorded in [dbo].[SchemaVersion], or null when the table does
    /// not exist yet (a new database, or one created before the table was added).
    /// </summary>
    public static async Task<string?> GetSchemaVersionAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await GetSchemaVersionAsync(connection, cancellationToken);
    }

    private static async Task<string?> GetSchemaVersionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'[dbo].[SchemaVersion]', N'U') IS NULL
                SELECT CAST(NULL AS nvarchar(20));
            ELSE
                SELECT TOP (1) [Version] FROM [dbo].[SchemaVersion] ORDER BY [UpdatedUtc] DESC;
            """;
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    private static async Task SetSchemaVersionAsync(SqlConnection connection, SqlTransaction transaction, string version, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'[dbo].[SchemaVersion]', N'U') IS NULL
                CREATE TABLE [dbo].[SchemaVersion]([Version] nvarchar(20) NOT NULL, [UpdatedUtc] datetime2(7) NOT NULL);

            UPDATE [dbo].[SchemaVersion] SET [Version] = @version, [UpdatedUtc] = SYSUTCDATETIME();
            IF @@ROWCOUNT = 0
                INSERT INTO [dbo].[SchemaVersion] ([Version], [UpdatedUtc]) VALUES (@version, SYSUTCDATETIME());
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { version }, transaction, cancellationToken: cancellationToken));
    }

    private static async Task<bool> HasExistingSchemaAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL THEN 0 ELSE 1 END");
        return exists == 1;
    }

    /// <summary>
    /// The folder the build copies the !SQL scripts to.
    /// </summary>
    public static string GetScriptDirectory() => Path.Combine(AppContext.BaseDirectory, ScriptFolderName);

    /// <summary>
    /// Returns the scripts whose version (file name) is newer than <paramref name="currentVersion"/>,
    /// in name order. A null or empty current version means every script is pending.
    /// </summary>
    public static IReadOnlyList<string> GetPendingScripts(IEnumerable<string> scriptPaths, string? currentVersion)
    {
        var current = ApplicationVersion.ToInteger(currentVersion);
        return scriptPaths
            .Where(path => string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase))
            .Where(path => ApplicationVersion.ToInteger(Path.GetFileNameWithoutExtension(path)) > current)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Splits a script into batches on lines that contain only GO.
    /// </summary>
    public static IEnumerable<string> SplitSqlBatches(string script)
    {
        var lines = script.Split(new[] {"\r\n", "\n"}, StringSplitOptions.None);
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                yield return sb.ToString();
                sb.Clear();
            }
            else
            {
                sb.AppendLine(line);
            }
        }
        if (sb.Length > 0)
            yield return sb.ToString();
    }
}
