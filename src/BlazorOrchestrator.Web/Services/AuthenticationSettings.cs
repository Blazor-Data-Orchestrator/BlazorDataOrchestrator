namespace BlazorOrchestrator.Web.Services;

public class AuthenticationSettings
{
    public bool IsMicrosoftConfigured { get; set; }
    public bool IsGoogleConfigured { get; set; }

    public void Refresh(bool microsoftConfigured, bool googleConfigured)
    {
        IsMicrosoftConfigured = microsoftConfigured;
        IsGoogleConfigured = googleConfigured;
    }
}
