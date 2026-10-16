namespace BlazorOrchestrator.Web.Services;

public class WizardStateService
{
    public int CurrentStep { get; private set; } = 1;
    public bool IsInstalling { get; set; } = false;
    public bool IsUpgrading { get; set; } = false;
    public bool IsAdminAuthenticated { get; private set; }
    public string AuthenticatedAdminName { get; private set; } = string.Empty;

    /// <summary>AspNetUsers.Id of the authorized Administrator, used to re-check the role before each run.</summary>
    public string AuthenticatedAdminUserId { get; private set; } = string.Empty;

    public event Action? OnChange;

    public void SetStep(int step)
    {
        CurrentStep = step;
        NotifyStateChanged();
    }

    public void SetInstalling(bool installing)
    {
        IsInstalling = installing;
        NotifyStateChanged();
    }

    public void SetUpgrading(bool upgrading)
    {
        IsUpgrading = upgrading;
        NotifyStateChanged();
    }

    public void SetAdminAuthenticated(string adminName, string? userId = null)
    {
        IsAdminAuthenticated = true;
        AuthenticatedAdminName = adminName;
        AuthenticatedAdminUserId = userId ?? string.Empty;
        NotifyStateChanged();
    }

    public void ClearAdminAuthenticated()
    {
        IsAdminAuthenticated = false;
        AuthenticatedAdminName = string.Empty;
        AuthenticatedAdminUserId = string.Empty;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
