using System.Collections;
using System.Text.Json;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Builds appsettings / config JSON content by reading environment variables
/// injected by Azure Container Apps / .NET Aspire.
/// Supports both C# (appsettings.json) and Python (config.json) formats.
/// </summary>
public static class AzureAppSettingsBuilder
{
    /// <summary>
    /// Well-known environment variable → config key mappings.
    /// Uses the standard .NET __ → : convention plus platform-specific keys.
    /// </summary>
    private static readonly Dictionary<string, string> KnownEnvVarMappings = new()
    {
        // Standard .NET double-underscore convention
        ["ConnectionStrings__blazororchestratordb"] = "blazororchestratordb",
        ["ConnectionStrings__blobs"]                = "blobs",
        ["ConnectionStrings__queues"]               = "queues",
        ["ConnectionStrings__tables"]               = "tables",
        // Azure/Aspire JDBC-style key
        ["BLAZORORCHESTRATORDB_JDBCCONNECTIONSTRING"] = "blazororchestratordb",
    };

    /// <summary>
    /// Resolves connection strings from environment variables.
    /// Returns null if no known environment variables are found.
    /// Shared by both C# and Python config builders.
    /// </summary>
    private static Dictionary<string, string>? ResolveConnectionStrings()
    {
        if (!AzureEnvironmentDetector.IsAzureContainerApp)
            return null;

        var connectionStrings = new Dictionary<string, string>();
        bool foundAny = false;

        foreach (var (envVar, key) in KnownEnvVarMappings)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(value))
                continue;

            foundAny = true;

            // Convert JDBC to ADO.NET if necessary
            if (envVar.Contains("JDBC", StringComparison.OrdinalIgnoreCase))
                value = ConvertJdbcToAdoNet(value);

            connectionStrings[key] = value;
        }

        // Also scan for any ConnectionStrings__* env vars we didn't explicitly map
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var envKey = entry.Key?.ToString() ?? "";
            if (envKey.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase))
            {
                var settingKey = envKey["ConnectionStrings__".Length..];
                if (!connectionStrings.ContainsKey(settingKey))
                {
                    connectionStrings[settingKey] = entry.Value?.ToString() ?? "";
                    foundAny = true;
                }
            }
        }

        return foundAny ? connectionStrings : null;
    }

    /// <summary>
    /// Builds a complete C# appsettings.json string from environment variables.
    /// Uses "ConnectionStrings" as the top-level key (standard .NET convention).
    /// Returns null if no known environment variables are found.
    /// </summary>
    public static string? BuildFromEnvironment()
    {
        var connectionStrings = ResolveConnectionStrings();
        if (connectionStrings == null)
            return null;

        var settingsObj = new Dictionary<string, object>
        {
            ["ConnectionStrings"] = connectionStrings,
            ["Logging"] = new Dictionary<string, object>
            {
                ["LogLevel"] = new Dictionary<string, string>
                {
                    ["Default"] = "Information",
                    ["Microsoft.AspNetCore"] = "Warning"
                }
            },
            ["AllowedHosts"] = "*"
        };

        return JsonSerializer.Serialize(settingsObj, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Builds a complete Python config.json string from environment variables.
    /// Uses "ConnectionStrings" as the top-level key to match the existing Python
    /// template which reads settings.get("ConnectionStrings", {}).
    /// Returns null if no known environment variables are found.
    /// </summary>
    public static string? BuildPythonConfigFromEnvironment()
    {
        var connectionStrings = ResolveConnectionStrings();
        if (connectionStrings == null)
            return null;

        // Use "ConnectionStrings" (PascalCase) to match the existing Python template
        // which reads: connection_strings = settings.get("ConnectionStrings", {})
        var configObj = new Dictionary<string, object>
        {
            ["ConnectionStrings"] = connectionStrings,
            ["Logging"] = new Dictionary<string, object>
            {
                ["Level"] = "INFO"
            }
        };

        return JsonSerializer.Serialize(configObj, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Converts a JDBC connection string to an ADO.NET connection string.
    /// Supports both SQL Server (jdbc:sqlserver://...) and PostgreSQL (jdbc:postgresql://...) formats.
    /// </summary>
    public static string ConvertJdbcToAdoNet(string jdbc)
    {
        if (string.IsNullOrWhiteSpace(jdbc))
            return jdbc;

        // Strip "jdbc:" prefix
        var raw = jdbc;
        if (raw.StartsWith("jdbc:", StringComparison.OrdinalIgnoreCase))
            raw = raw[5..];

        // SQL Server: jdbc:sqlserver://host:port;database=...;user=...;password=...
        if (jdbc.Contains("sqlserver", StringComparison.OrdinalIgnoreCase))
            return ConvertJdbcSqlServer(jdbc);

        // PostgreSQL: jdbc:postgresql://host:port/dbname?user=x&password=y
        if (jdbc.Contains("postgresql", StringComparison.OrdinalIgnoreCase))
            return ConvertJdbcPostgresql(raw);

        // Unknown format — return as-is
        return jdbc;
    }

    private static string ConvertJdbcSqlServer(string jdbc)
    {
        // jdbc:sqlserver://host:port;property=value;...
        // Extract the part after "jdbc:sqlserver://"
        var prefix = "jdbc:sqlserver://";
        var idx = jdbc.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return jdbc;

        var remainder = jdbc[(idx + prefix.Length)..];

        // Split host:port from properties
        var semiIdx = remainder.IndexOf(';');
        var hostPort = semiIdx >= 0 ? remainder[..semiIdx] : remainder;
        var props = semiIdx >= 0 ? remainder[(semiIdx + 1)..] : "";

        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1] : "1433";

        // Parse properties (key=value;key=value)
        var propDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = prop.IndexOf('=');
            if (eqIdx > 0)
                propDict[prop[..eqIdx].Trim()] = prop[(eqIdx + 1)..].Trim();
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"Server={host},{port}");
        if (propDict.TryGetValue("database", out var db) ||
            propDict.TryGetValue("databaseName", out db))
            sb.Append($";Database={db}");
        if (propDict.TryGetValue("user", out var user) ||
            propDict.TryGetValue("userName", out user))
            sb.Append($";User ID={user}");
        if (propDict.TryGetValue("password", out var pw))
            sb.Append($";Password={pw}");
        sb.Append(";TrustServerCertificate=true");

        return sb.ToString();
    }

    private static string ConvertJdbcPostgresql(string raw)
    {
        try
        {
            var uri = new Uri(raw);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var database = uri.AbsolutePath.TrimStart('/');
            var user = query["user"] ?? query["username"] ?? "";
            var password = query["password"] ?? "";

            return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require";
        }
        catch
        {
            return raw;
        }
    }
}
