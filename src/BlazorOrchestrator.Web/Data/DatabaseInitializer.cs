using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Data;

/// <summary>
/// Creates and upgrades the BlazorOrchestrator database schema.
/// <list type="bullet">
/// <item><see cref="EnsureDatabaseAsync"/> (startup and the Install Wizard) only ever creates a
/// <b>new</b> schema from the baseline script. It never changes an existing schema.</item>
/// <item><see cref="ApplyPendingScriptsAsync"/> (the Upgrade Wizard, after an Administrator signs
/// in) applies the newer !SQL scripts to an existing schema.</item>
/// </list>
/// Both take <see cref="SchemaMigrationLock"/> before reading the schema version, so two replicas,
/// two wizard sessions, or startup and a wizard never run scripts at the same time.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>Table used to detect whether the baseline schema exists.</summary>
    private const string SchemaMarkerTable = "[dbo].[AspNetUsers]";

    public static async Task EnsureDatabaseAsync(IConfiguration configuration, ILogger logger, Action<string>? onProgress = null, SettingsService? settingsService = null, string? currentVersion = null)
    {
        var connStr = configuration.GetConnectionString("blazororchestratordb");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            var msg = "No blazororchestratordb connection string found; skipping database initialization.";
            logger.LogWarning(msg);
            onProgress?.Invoke($"⚠️ {msg}");
            return;
        }

        var versionStore = settingsService != null ? new SettingsSchemaVersionStore(settingsService) : null;
        await InitializeDatabaseAsync(connStr, logger, onProgress, versionStore, currentVersion);
    }

    /// <summary>
    /// Creates the database (when the login may) and, when the database has no schema yet, the
    /// baseline schema in one transaction. An existing schema is left unchanged.
    /// </summary>
    /// <returns>True when the schema was created by this call.</returns>
    public static async Task<bool> InitializeDatabaseAsync(
        string connStr,
        ILogger logger,
        Action<string>? onProgress = null,
        ISchemaVersionStore? versionStore = null,
        string? currentVersion = null,
        string? scriptDirectory = null,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default)
    {
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
                    await masterConnection.OpenAsync(cancellationToken);
                    onProgress?.Invoke("✅ Connected to SQL Server (master database).");

                    // Check if database exists
                    var dbExistsQuery = "SELECT database_id FROM sys.databases WHERE name = @dbName";
                    var dbExists = await masterConnection.ExecuteScalarAsync<int?>(dbExistsQuery, new { dbName = databaseName });

                    if (dbExists == null)
                    {
                        onProgress?.Invoke($"📦 Creating database '{databaseName}'...");
                        await masterConnection.ExecuteAsync($"CREATE DATABASE [{databaseName.Replace("]", "]]")}]");
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

            // Now connect to the actual database
            onProgress?.Invoke($"🔌 Connecting to database '{databaseName}'...");
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);
            onProgress?.Invoke($"✅ Connected to database '{databaseName}'.");

            onProgress?.Invoke("🔒 Waiting for the schema lock...");
            await using var schemaLock = await SchemaMigrationLock.AcquireAsync(connection, lockTimeout, cancellationToken);

            // Checked under the lock: another replica may have just created the schema.
            var created = false;
            if (await SchemaExistsAsync(connection, cancellationToken))
            {
                // Never change an existing schema here: newer scripts are applied only by the
                // Upgrade Wizard after an Administrator signs in.
                onProgress?.Invoke("ℹ️ The database schema already exists; it is not changed here. Newer scripts are applied by the Upgrade Wizard.");
            }
            else
            {
                onProgress?.Invoke("📄 Loading SQL baseline script...");
                var directory = scriptDirectory ?? SqlScriptCatalog.DefaultDirectory;
                var path = Path.Combine(directory, SqlScriptCatalog.BaselineScript);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("SQL initialization script not found.", path);
                }

                var batches = SqlScriptCatalog.GetBatches(await File.ReadAllTextAsync(path, cancellationToken));
                onProgress?.Invoke($"📊 Found {batches.Count} SQL batches to execute.");

                // One transaction: a failure leaves no half-created schema behind.
                await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken))
                {
                    var batchIndex = 0;
                    foreach (var batch in batches)
                    {
                        batchIndex++;
                        onProgress?.Invoke($"⚡ Executing batch {batchIndex}/{batches.Count}...");
                        await ExecuteBatchAsync(connection, transaction, batch, cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                }

                created = true;
                var successMsg = "Database initialization script executed successfully.";
                logger.LogInformation(successMsg);
                onProgress?.Invoke($"✅ {successMsg}");
            }

            // The SchemaVersion setting, written while the lock is still held.
            if (versionStore != null && !string.IsNullOrEmpty(currentVersion))
            {
                try
                {
                    var existingVersion = await versionStore.GetAsync(cancellationToken);
                    if (created)
                    {
                        // A new schema is the full baseline of the running code.
                        onProgress?.Invoke($"📝 Setting SchemaVersion to {currentVersion}...");
                        await versionStore.SetAsync(currentVersion, cancellationToken);
                        onProgress?.Invoke($"✅ SchemaVersion set to {currentVersion}.");
                    }
                    else if (string.IsNullOrEmpty(existingVersion))
                    {
                        // An existing schema with no recorded version predates version tracking:
                        // record the baseline so the Upgrade Wizard applies the newer scripts
                        // (they are idempotent) instead of silently marking it as current.
                        onProgress?.Invoke($"📝 No SchemaVersion recorded for the existing schema; recording {SqlScriptCatalog.BaselineVersion}.");
                        await versionStore.SetAsync(SqlScriptCatalog.BaselineVersion, cancellationToken);
                    }
                    else
                    {
                        onProgress?.Invoke($"ℹ️ SchemaVersion already set to {existingVersion}; leaving unchanged.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to set SchemaVersion in Azure Table Storage.");
                    onProgress?.Invoke($"⚠️ Could not set SchemaVersion: {ex.Message}");
                }
            }

            onProgress?.Invoke("🎉 Database setup complete!");
            return created;
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
    /// Applies every !SQL script newer than the recorded schema version (up to
    /// <paramref name="codeVersion"/>) in version order. Each script runs in its own transaction;
    /// the version is recorded after each script commits, then raised to the code version.
    /// Only the Upgrade Wizard calls this, after it has verified an Administrator.
    /// </summary>
    /// <exception cref="MissingSqlScriptsException">Scripts this build shipped with are missing; nothing is changed.</exception>
    /// <exception cref="SchemaScriptFailedException">A script failed; it was rolled back and earlier scripts stay applied.</exception>
    public static async Task<SchemaUpgradeResult> ApplyPendingScriptsAsync(
        string connectionString,
        string codeVersion,
        ISchemaVersionStore versionStore,
        ILogger logger,
        Action<string>? onProgress = null,
        string? scriptDirectory = null,
        IEnumerable<string>? expectedScripts = null,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var directory = scriptDirectory ?? SqlScriptCatalog.DefaultDirectory;

        // A deployment without its scripts must not record a version it never applied.
        var missing = SqlScriptCatalog.FindMissing(directory, expectedScripts ?? SqlScriptCatalog.ExpectedScripts);
        if (missing.Count > 0)
        {
            throw new MissingSqlScriptsException(directory, missing);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        onProgress?.Invoke("Waiting for the schema lock...");
        await using var schemaLock = await SchemaMigrationLock.AcquireAsync(connection, lockTimeout, cancellationToken);

        // Read under the lock: another session may have upgraded while this one waited.
        var startVersion = await versionStore.GetAsync(cancellationToken) ?? SqlScriptCatalog.BaselineVersion;
        var applied = new List<string>();

        if (ApplicationVersion.ToInteger(startVersion) >= ApplicationVersion.ToInteger(codeVersion))
        {
            onProgress?.Invoke($"The database is already at version {startVersion}; nothing to apply.");
            return new SchemaUpgradeResult(startVersion, startVersion, applied);
        }

        var pending = SqlScriptCatalog.GetPendingScripts(Directory.EnumerateFiles(directory, "*.sql"), startVersion, codeVersion);
        onProgress?.Invoke(pending.Count == 0
            ? $"No migration scripts between {startVersion} and {codeVersion}."
            : $"Found {pending.Count} migration script(s) to apply: {string.Join(", ", pending.Select(Path.GetFileName))}.");

        foreach (var path in pending)
        {
            var name = Path.GetFileName(path);
            var version = SqlScriptCatalog.VersionOf(path);
            var batches = SqlScriptCatalog.GetBatches(await File.ReadAllTextAsync(path, cancellationToken));
            onProgress?.Invoke($"Running {name} ({batches.Count} batches)...");

            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken))
            {
                for (var i = 0; i < batches.Count; i++)
                {
                    try
                    {
                        await ExecuteBatchAsync(connection, transaction, batches[i], cancellationToken);
                    }
                    catch (Exception ex) when (ex is SqlException or InvalidOperationException)
                    {
                        logger.LogError(ex, "Schema script {Script} failed in batch {Batch}; rolled back.", name, i + 1);
                        throw new SchemaScriptFailedException(name, i + 1, ex);
                    }
                }

                await transaction.CommitAsync(cancellationToken);
            }

            await versionStore.SetAsync(version, cancellationToken);
            applied.Add(name);
            logger.LogInformation("Applied schema script {Script}.", name);
            onProgress?.Invoke($"✓ {name} applied. SchemaVersion is now {version}.");
        }

        // A version bump without a script only records the new version.
        await versionStore.SetAsync(codeVersion, cancellationToken);
        onProgress?.Invoke($"SchemaVersion updated to {codeVersion}.");
        return new SchemaUpgradeResult(startVersion, codeVersion, applied);
    }

    private static async Task<bool> SchemaExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"SELECT CASE WHEN OBJECT_ID(N'{SchemaMarkerTable}', N'U') IS NULL THEN 0 ELSE 1 END", connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    private static async Task ExecuteBatchAsync(SqlConnection connection, SqlTransaction transaction, string batch, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(batch, connection, transaction) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record SchemaUpgradeResult(string PreviousVersion, string CurrentVersion, IReadOnlyList<string> AppliedScripts);

/// <summary>The deployment is missing !SQL scripts the build contained; it must be redeployed.</summary>
public sealed class MissingSqlScriptsException(string directory, IReadOnlyList<string> missingScripts)
    : Exception($"The database upgrade cannot run because these schema scripts are missing from '{directory}': {string.Join(", ", missingScripts)}. " +
                "Redeploy the release (or restore the !SQL folder) and open the Upgrade Wizard again.")
{
    public string Directory { get; } = directory;
    public IReadOnlyList<string> MissingScripts { get; } = missingScripts;
}

/// <summary>A schema script failed; its transaction was rolled back.</summary>
public sealed class SchemaScriptFailedException(string script, int batch, Exception inner)
    : Exception($"{script} failed in batch {batch} and was rolled back: {inner.Message}", inner)
{
    public string Script { get; } = script;
    public int Batch { get; } = batch;
}
