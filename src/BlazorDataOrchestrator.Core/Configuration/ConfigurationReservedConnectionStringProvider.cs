using BlazorDataOrchestrator.Core.Services;
using Microsoft.Extensions.Configuration;

namespace BlazorDataOrchestrator.Core.Configuration;

/// <summary>
/// Resolves the reserved connection strings from environment variables injected by
/// Azure Container Apps / Aspire first, then from <see cref="IConfiguration"/>.
/// </summary>
public sealed class ConfigurationReservedConnectionStringProvider : IReservedConnectionStringProvider
{
    private readonly IConfiguration _configuration;

    public ConfigurationReservedConnectionStringProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ReservedConnectionStrings Get()
    {
        // IConfiguration is canonical: Aspire and the env-var provider already feed it.
        // The raw env scan is only a fallback for non-standard keys such as the JDBC-style one.
        var fromEnvironment = AzureAppSettingsBuilder.ResolveConnectionStrings()
                              ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string Resolve(string key)
        {
            var fromConfig = _configuration.GetConnectionString(key);
            if (!string.IsNullOrWhiteSpace(fromConfig))
                return fromConfig;

            return fromEnvironment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : string.Empty;
        }

        return new ReservedConnectionStrings(
            Blobs: Resolve(ReservedConnectionStrings.BlobsKey),
            Queues: Resolve(ReservedConnectionStrings.QueuesKey),
            Tables: Resolve(ReservedConnectionStrings.TablesKey),
            BlazorOrchestratorDb: Resolve(ReservedConnectionStrings.DbKey));
    }
}
