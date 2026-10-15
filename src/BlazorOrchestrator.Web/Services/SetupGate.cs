namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Decides which requests bypass the "redirect to /setup" gate in Program.cs. The setup pages,
/// sign-in, Blazor's own endpoints and static assets must work while the system is not configured
/// or needs an upgrade. The health endpoints must answer too: after a deployment the database
/// usually needs the Upgrade Wizard, and BlazorData.net's liveness check (/alive) must still get
/// a 200 instead of a redirect.
/// </summary>
public static class SetupGate
{
    private static readonly string[] ExemptPrefixes =
    [
        "/setup",
        "/account",
        "/community",
        "/_blazor",
        "/_framework",
        "/alive",
        "/health"
    ];

    public static bool IsExempt(PathString path, bool isStaticAsset)
    {
        if (isStaticAsset)
        {
            return true;
        }

        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
