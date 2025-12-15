using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace BlazorOrchistrator.Web.Data;

public static class DatabaseInitializer
{
    public static async Task EnsureDatabaseAsync(IConfiguration configuration, ILogger logger)
    {
        var connStr = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            logger.LogWarning("No DefaultConnection string found; skipping database initialization.");
            return;
        }

        try
        {
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync();

            // Read embedded SQL script
            var script = await GetSqlScriptAsync();

            // Split on GO batches (simple parser)
            var batches = SplitSqlBatches(script);
            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await connection.ExecuteAsync(batch);
            }
            logger.LogInformation("Database initialization script executed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database schema.");
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
