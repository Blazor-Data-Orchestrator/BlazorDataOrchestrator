namespace BlazorOrchestrator.Web.Services;

public interface ISystemStatusService
{
    Task<bool> IsConfiguredAsync();
    Task<bool> NeedsUpgradeAsync();
    void Reset();
}
