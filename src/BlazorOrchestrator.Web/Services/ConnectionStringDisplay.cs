using System.Data.Common;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Describes a connection string for display without any secret. It is an allow-list: only the
/// server, database, storage account or endpoint host are returned, read with
/// <see cref="DbConnectionStringBuilder"/> so quoted values (<c>Password="a;b"</c>,
/// <c>Password='it''s'</c>) are parsed as whole values. Anything that cannot be parsed is
/// shown as hidden, never echoed back.
/// </summary>
public static class ConnectionStringDisplay
{
    public const string Hidden = "(connection string hidden)";

    private static readonly string[] ServerKeys = ["Server", "Data Source", "Address", "Addr", "Network Address"];
    private static readonly string[] DatabaseKeys = ["Database", "Initial Catalog"];

    public static string Describe(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "(not set)";
        }

        var value = connectionString.Trim();

        // Endpoint URIs (managed identity): scheme and host only; a SAS query or user info is dropped.
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : ":" + uri.Port)}";
        }

        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = value };
        }
        catch (ArgumentException)
        {
            return Hidden;
        }

        if (TryGet(builder, "UseDevelopmentStorage", out var development) && development.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return "Local storage emulator (UseDevelopmentStorage=true)";
        }

        if (TryGet(builder, "AccountName", out var account))
        {
            return $"Storage account: {account}";
        }

        foreach (var endpointKey in new[] { "BlobEndpoint", "TableEndpoint", "QueueEndpoint" })
        {
            if (TryGet(builder, endpointKey, out var endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            {
                return $"Storage endpoint: {endpointUri.Host}";
            }
        }

        var server = ServerKeys.Select(key => TryGet(builder, key, out var v) ? v : null).FirstOrDefault(v => v != null);
        if (server != null)
        {
            var database = DatabaseKeys.Select(key => TryGet(builder, key, out var v) ? v : null).FirstOrDefault(v => v != null);
            return database != null ? $"Server: {server}; Database: {database}" : $"Server: {server}";
        }

        return Hidden;
    }

    private static bool TryGet(DbConnectionStringBuilder builder, string key, out string value)
    {
        if (builder.TryGetValue(key, out var raw) && raw?.ToString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text))
        {
            value = text.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
