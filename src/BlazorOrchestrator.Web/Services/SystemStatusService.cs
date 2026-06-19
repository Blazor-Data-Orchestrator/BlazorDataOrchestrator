using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

public class SystemStatusService : ISystemStatusService
{
    private bool? _isConfigured;
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

    public void Reset()
    {
        _isConfigured = null;
    }
}
