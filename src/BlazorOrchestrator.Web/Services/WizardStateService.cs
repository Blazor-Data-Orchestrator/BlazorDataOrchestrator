namespace BlazorOrchestrator.Web.Services;

public class WizardStateService
{
    public int CurrentStep { get; private set; } = 1;
    public bool IsInstalling { get; set; } = false;
    public bool IsUpgrading { get; set; } = false;

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

    private void NotifyStateChanged() => OnChange?.Invoke();
}
