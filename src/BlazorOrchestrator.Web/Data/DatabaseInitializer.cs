using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace BlazorOrchestrator.Web.Data;

public static class DatabaseInitializer
{
    public static async Task EnsureDatabaseAsync(IConfiguration configuration, ILogger logger, Action<string>? onProgress = null)
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

            // Now connect to the actual database and run scripts
            onProgress?.Invoke($"🔌 Connecting to database '{databaseName}'...");
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync();
            onProgress?.Invoke($"✅ Connected to database '{databaseName}'.");

            // Read embedded SQL script
            onProgress?.Invoke("📄 Loading SQL initialization script...");
            var script = await GetSqlScriptAsync();
            onProgress?.Invoke("✅ SQL script loaded.");

            // Split on GO batches (simple parser)
            var batches = SplitSqlBatches(script).ToList();
            onProgress?.Invoke($"📊 Found {batches.Count} SQL batches to execute.");

            int batchIndex = 0;
            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                batchIndex++;
                onProgress?.Invoke($"⚡ Executing batch {batchIndex}/{batches.Count}...");
                await connection.ExecuteAsync(batch);
            }
            
            var successMsg = "Database initialization script executed successfully.";
            logger.LogInformation(successMsg);
            onProgress?.Invoke($"✅ {successMsg}");
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

    private static async Task<string> GetSqlScriptAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "!SQL", "01.00.00.sql");
        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path);
        }
        throw new FileNotFoundException("SQL initialization script not found.", path);
    }

    private static IEnumerable<string> SplitSqlBatches(string script)
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
