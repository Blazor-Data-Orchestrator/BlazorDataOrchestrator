namespace BlazorOrchestrator.Web.Services.Community;

/// <summary>
/// Registers this deployment with the Community Jobs Library and refreshes the heartbeat periodically.
/// Registration only succeeds while a user has a valid community token, so failures are expected and
/// non-fatal.
/// </summary>
public sealed class InstallationHeartbeatService(
    IServiceScopeFactory scopeFactory,
    ILogger<InstallationHeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<ICommunityClient>();
                var installation = await client.RegisterInstallationAsync(stoppingToken);

                if (installation is not null)
                {
                    logger.LogDebug("Community installation heartbeat sent for {InstallationId}", installation.InstallationId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "The community installation heartbeat failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
