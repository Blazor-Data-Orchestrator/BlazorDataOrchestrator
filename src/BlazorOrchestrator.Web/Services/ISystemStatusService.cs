namespace BlazorOrchestrator.Web.Services;

public interface ISystemStatusService
{
    Task<bool> IsConfiguredAsync();
    void Reset();
}
