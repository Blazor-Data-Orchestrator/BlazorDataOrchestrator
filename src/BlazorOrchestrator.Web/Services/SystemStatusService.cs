using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

public class SystemStatusService : ISystemStatusService
{
    private bool? _isConfigured;
    private bool? _needsUpgrade;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SystemStatusService> _logger;

    public SystemStatusService(IServiceProvider serviceProvider, ILogger<SystemStatusService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        if (_isConfigured.HasValue)
            return _isConfigured.Value;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var canConnect = await dbContext.Database.CanConnectAsync(cts.Token);
            if (!canConnect)
            {
                _isConfigured = false;
                return false;
            }

            // Check if tables exist and admin user is present
            var hasUsers = await dbContext.AspNetUsers.AsNoTracking().AnyAsync(cts.Token);
            _isConfigured = hasUsers;
            return hasUsers;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "System status check failed - system not configured");
            _isConfigured = false;
            return false;
        }
    }

    public async Task<bool> NeedsUpgradeAsync()
    {
        if (_needsUpgrade.HasValue)
            return _needsUpgrade.Value;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();

            var schemaVersion = await settingsService.GetOrDefaultAsync(
                "SchemaVersion", ApplicationVersion.Current);

            _needsUpgrade = ConvertVersionToInteger(ApplicationVersion.Current)
                          > ConvertVersionToInteger(schemaVersion);
            return _needsUpgrade.Value;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Schema version check failed — assuming no upgrade needed");
            _needsUpgrade = false;
            return false;
        }
    }

    public void Reset()
    {
        _isConfigured = null;
        _needsUpgrade = null;
    }

    private static int ConvertVersionToInteger(string version)
    {
        if (string.IsNullOrEmpty(version)) return 0;
        int result = 0;
        var segments = version.Split('.');
        var multipliers = new[] { 10000, 100, 1 };
        for (int i = 0; i < segments.Length && i < multipliers.Length; i++)
        {
            if (int.TryParse(segments[i], out int segment))
                result += segment * multipliers[i];
        }
        return result;
    }
}
