using Azure;
using Azure.Data.Tables;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Services;

public enum InstallMarkerState
{
    NotInstalled,
    Installed,
    Unknown
}

/// <summary>
/// Durable record that installation finished, kept in the Settings table. Once it exists the
/// anonymous setup wizard refuses install and create-admin modes, so a transient database
/// outage cannot reopen it (SEC-005).
/// </summary>
public sealed class InstallMarkerService
{
    public const string SettingKey = "InstallCompleted";
    private const string SettingsTable = "Settings";
    private const string SettingsPartition = "AppSettings";

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InstallMarkerService> _logger;
    private volatile bool _knownInstalled;

    public InstallMarkerService(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<InstallMarkerService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<InstallMarkerState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (_knownInstalled)
        {
            return InstallMarkerState.Installed;
        }

        TableServiceClient client;
        try
        {
            client = _serviceProvider.GetRequiredService<TableServiceClient>();
        }
        catch (Exception ex)
        {
            // Without table storage configured this is a first install; with it configured we cannot tell.
            _logger.LogDebug(ex, "Table storage client unavailable while reading the install marker.");
            return string.IsNullOrWhiteSpace(_configuration.GetConnectionString("tables"))
                ? InstallMarkerState.NotInstalled
                : InstallMarkerState.Unknown;
        }

        try
        {
            var response = await client.GetTableClient(SettingsTable)
                .GetEntityIfExistsAsync<TableEntity>(SettingsPartition, SettingKey, cancellationToken: cancellationToken);
            if (response.HasValue)
            {
                _knownInstalled = true;
                return InstallMarkerState.Installed;
            }
            return InstallMarkerState.NotInstalled;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return InstallMarkerState.NotInstalled;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the install marker; treating installation state as unknown.");
            return InstallMarkerState.Unknown;
        }
    }

    /// <summary>Records that the system is installed. Failures are logged, never thrown.</summary>
    public async Task MarkInstalledAsync()
    {
        if (_knownInstalled)
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
            if (await settings.GetAsync(SettingKey) == null)
            {
                await settings.SetAsync(SettingKey, DateTime.UtcNow.ToString("O"),
                    "Installation completed; the anonymous setup wizard stays closed while this exists");
            }
            _knownInstalled = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the install marker.");
        }
    }
}

/// <summary>Decides which setup wizard mode a visitor may see.</summary>
public static class SetupAccessPolicy
{
    public const string Unavailable = "UNAVAILABLE";

    private static readonly HashSet<string> InstallModes = new(StringComparer.Ordinal)
    {
        "INSTALL", "RUNSCRIPTS", "CreateAdministrator"
    };

    /// <summary>
    /// Install-type modes are only offered anonymously on a system that has never been installed.
    /// If the system was installed, or that cannot be determined, only an Admin may see them.
    /// </summary>
    public static string Resolve(string detectedMode, InstallMarkerState marker, bool isAdmin)
    {
        if (!InstallModes.Contains(detectedMode) || isAdmin || marker == InstallMarkerState.NotInstalled)
        {
            return detectedMode;
        }

        return Unavailable;
    }
}
