using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Durable record that the installation is role-aware (it has, or had, an Admin chosen on
/// purpose). Once it exists, users are never promoted to Admin automatically again.
/// </summary>
public interface IAdminRoleMigrationMarker
{
    /// <summary>True when recorded. Throws when the state cannot be read (callers fail closed).</summary>
    Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default);

    /// <summary>Records the marker. Throws when it cannot be written.</summary>
    Task MarkCompletedAsync(CancellationToken cancellationToken = default);
}

/// <summary>The marker as the <c>AdminRoleMigrationCompleted</c> setting in Table Storage.</summary>
public sealed class SettingsAdminRoleMigrationMarker(SettingsService settingsService) : IAdminRoleMigrationMarker
{
    public async Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default) =>
        await settingsService.GetStrictAsync(AllowedUserService.AdminRoleMigrationSettingKey, cancellationToken) != null;

    public Task MarkCompletedAsync(CancellationToken cancellationToken = default) =>
        settingsService.SetAsync(AllowedUserService.AdminRoleMigrationSettingKey, DateTime.UtcNow.ToString("O"),
            "Admin role migration completed; users are no longer promoted automatically");
}
