using Microsoft.AspNetCore.DataProtection;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Generates and validates short-lived encrypted tokens for the upgrade wizard
/// external authentication flow. Tokens are encrypted using ASP.NET Core Data Protection
/// and expire after 5 minutes.
/// </summary>
public class WizardTokenService
{
    private const string Purpose = "UpgradeWizard";
    private readonly IDataProtectionProvider _dataProtection;

    public WizardTokenService(IDataProtectionProvider dataProtection)
    {
        _dataProtection = dataProtection;
    }

    public string GenerateToken(string userId, string userName)
    {
        var protector = _dataProtection.CreateProtector(Purpose);
        var payload = $"{userId}|{userName}|{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}";
        return protector.Protect(payload);
    }

    public (bool IsValid, string UserId, string UserName) ValidateToken(string token)
    {
        try
        {
            var protector = _dataProtection.CreateProtector(Purpose);
            var payload = protector.Unprotect(token);
            var parts = payload.Split('|');

            if (parts.Length != 3) return (false, "", "");

            var expiry = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[2]));
            if (expiry < DateTimeOffset.UtcNow) return (false, "", "");

            return (true, parts[0], parts[1]);
        }
        catch
        {
            return (false, "", "");
        }
    }
}
