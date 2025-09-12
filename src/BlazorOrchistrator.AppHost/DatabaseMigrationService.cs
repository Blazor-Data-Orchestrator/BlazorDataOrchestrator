using Microsoft.Data.SqlClient;
using System.Reflection;

namespace BlazorOrchistrator.AppHost;

public static class DatabaseMigrationService
{
    public static async Task RunAdvancedMigrationsAsync(string connectionString)
    {
        try
        {
            // Read the advanced features script
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var appDirectory = Path.GetDirectoryName(assemblyLocation) ?? Directory.GetCurrentDirectory();
            var advancedScriptPath = Path.Combine(appDirectory, "!SQL", "02.00.00-advanced.sql");
            
            if (!File.Exists(advancedScriptPath))
            {
                Console.WriteLine($"Advanced migration script not found at: {advancedScriptPath}");
                return;
            }

            var advancedScript = await File.ReadAllTextAsync(advancedScriptPath);
            
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            // Split the script by GO statements and execute each batch
            var batches = advancedScript.Split(new[] { "\nGO\n", "\nGO\r\n", "\r\nGO\r\n", "\r\nGO\n" }, 
                StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var batch in batches)
            {
                var trimmedBatch = batch.Trim();
                if (string.IsNullOrEmpty(trimmedBatch)) continue;
                
                using var command = new SqlCommand(trimmedBatch, connection);
                command.CommandTimeout = 120; // 2 minutes timeout for complex operations
                await command.ExecuteNonQueryAsync();
            }
            
            Console.WriteLine("Advanced database migrations completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running advanced migrations: {ex.Message}");
            // Don't throw - this is optional advanced functionality
        }
    }
}