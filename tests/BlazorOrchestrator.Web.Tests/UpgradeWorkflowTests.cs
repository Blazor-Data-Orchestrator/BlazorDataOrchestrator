using Azure.Data.Tables;
using Bunit;
using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Web.Components.Pages.InstallUpgrade;
using BlazorOrchestrator.Web.Data.Data;
using BlazorOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>Upgrade Wizard (UpgradeWorkflow.razor) authorization and failure handling.</summary>
public class UpgradeWorkflowTests : BunitContext
{
    private const string AdminPassword = "Correct-Horse-1";

    private readonly string _scripts = Directory.CreateTempSubdirectory("bdo-upgrade-").FullName;
    private readonly ApplicationDbContext _db;
    private readonly WizardStateService _wizardState = new();

    public UpgradeWorkflowTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddRadzenComponents();
        Services.AddLogging();

        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"upgrade-{Guid.NewGuid():N}")
            .Options);

        Services.AddSingleton(_wizardState);
        Services.AddSingleton(_db);
        Services.AddSingleton(new AuthService(_db));
        Services.AddSingleton(new AuthenticationSettings());
        Services.AddSingleton(new SettingsService(new TableServiceClient("UseDevelopmentStorage=true")));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blazororchestratordb"] = "Server=tcp:127.0.0.1,1;Database=x;Connect Timeout=1" })
            .Build());

        File.WriteAllText(Path.Combine(_scripts, "01.00.00.sql"), "SELECT 1");
        File.WriteAllText(Path.Combine(_scripts, "01.20.00.sql"), "SELECT 1");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_scripts, recursive: true); } catch { }
    }

    private IRenderedComponent<UpgradeWorkflow> RenderWizard(bool isAdminAuthenticated = false, IReadOnlyList<string>? expected = null) =>
        Render<UpgradeWorkflow>(parameters => parameters
            .Add(p => p.CurrentCodeVersion, "01.60.00")
            .Add(p => p.DatabaseVersion, "01.10.00")
            .Add(p => p.IsAdminAuthenticated, isAdminAuthenticated)
            .Add(p => p.ScriptDirectory, _scripts)
            .Add(p => p.ExpectedScripts, expected ?? ["01.00.00.sql", "01.20.00.sql"]));

    private async Task<AspNetUser> AddUserAsync(string id, bool admin, bool enabled = true)
    {
        var user = new AspNetUser
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Email = $"{id}@example.com",
            NormalizedEmail = $"{id}@example.com".ToUpperInvariant(),
            EmailConfirmed = enabled
        };
        user.PasswordHash = new PasswordHasher<AspNetUser>().HashPassword(user, AdminPassword);
        if (admin)
        {
            var role = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.NormalizedName == "ADMIN")
                       ?? new AspNetRole { Id = "admin-role", Name = "Admin", NormalizedName = "ADMIN" };
            user.Roles.Add(role);
        }
        _db.AspNetUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static void SignIn(IRenderedComponent<UpgradeWorkflow> cut, string user, string password)
    {
        cut.Find("input[placeholder='Username or Email']").Change(user);
        cut.Find("input[type=password]").Change(password);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Authorize Upgrade")).Click();
    }

    [Fact(DisplayName = "Review #1: without an authorized Administrator the wizard shows only the sign-in and nothing that leaves the wizard")]
    [Trait("Category", "Security")]
    public void Anonymous_SeesOnlySignIn()
    {
        var cut = RenderWizard();

        Assert.Equal("login", cut.Instance.Stage);
        Assert.Contains("Administrator Authentication", cut.Markup);
        Assert.DoesNotContain("Apply Upgrade", cut.Markup);
        Assert.DoesNotContain("Continue", cut.Markup);
    }

    [Fact(DisplayName = "Upgrade Wizard: a non-admin user cannot authorize the upgrade")]
    [Trait("Category", "Security")]
    public async Task NonAdmin_IsRejected()
    {
        await AddUserAsync("viewer", admin: false);
        var cut = RenderWizard();

        SignIn(cut, "viewer", AdminPassword);

        Assert.Equal("login", cut.Instance.Stage);
        Assert.Contains("Only administrators", cut.Markup);
    }

    [Fact(DisplayName = "Upgrade Wizard: an Administrator sees the scripts that will run")]
    [Trait("Category", "Upgrade")]
    public async Task Admin_SeesPendingScripts()
    {
        await AddUserAsync("boss", admin: true);
        var cut = RenderWizard();

        SignIn(cut, "boss", AdminPassword);

        Assert.Equal("check", cut.Instance.Stage);
        Assert.Contains("01.20.00.sql", cut.Markup);
        Assert.DoesNotContain("01.00.00.sql", cut.Markup);
        Assert.Equal("boss", _wizardState.AuthenticatedAdminUserId);
    }

    [Fact(DisplayName = "Review #7: a database outage during sign-in shows a recoverable error instead of ending the circuit")]
    [Trait("Category", "Security")]
    public async Task SignIn_DatabaseFailure_IsRecoverable()
    {
        var cut = RenderWizard();
        await _db.DisposeAsync();

        SignIn(cut, "boss", AdminPassword);

        Assert.Equal("login", cut.Instance.Stage);
        Assert.Contains("Could not verify the administrator account", cut.Markup);
    }

    [Fact(DisplayName = "Review #7: a database outage in the re-check before Apply Upgrade is caught and shown")]
    [Trait("Category", "Security")]
    public async Task ApplyUpgrade_AuthorizationCheckFailure_IsRecoverable()
    {
        await AddUserAsync("boss", admin: true);
        _wizardState.SetAdminAuthenticated("boss", "boss");
        var cut = RenderWizard(isAdminAuthenticated: true);
        Assert.Equal("check", cut.Instance.Stage);
        await _db.DisposeAsync();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Apply Upgrade")).Click();

        Assert.Equal("check", cut.Instance.Stage);
        Assert.Contains("Could not verify the administrator account", cut.Markup);
    }

    [Fact(DisplayName = "Upgrade Wizard: a user who lost the Admin role is sent back to sign in before any script runs")]
    [Trait("Category", "Security")]
    public async Task ApplyUpgrade_RoleRemoved_ReturnsToSignIn()
    {
        var user = await AddUserAsync("boss", admin: true);
        _wizardState.SetAdminAuthenticated("boss", "boss");
        var cut = RenderWizard(isAdminAuthenticated: true);
        user.Roles.Clear();
        await _db.SaveChangesAsync();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Apply Upgrade")).Click();

        Assert.Equal("login", cut.Instance.Stage);
        Assert.False(_wizardState.IsAdminAuthenticated);
    }

    [Fact(DisplayName = "Review #8: missing !SQL scripts show a deployment error with no way to skip the upgrade")]
    [Trait("Category", "Upgrade")]
    public void MissingScripts_ShowDeploymentError()
    {
        _wizardState.SetAdminAuthenticated("boss", "boss");

        var cut = RenderWizard(isAdminAuthenticated: true, expected: ["01.00.00.sql", "01.10.00.sql", "01.20.00.sql"]);

        Assert.Equal("missing", cut.Instance.Stage);
        Assert.Contains("01.10.00.sql", cut.Markup);
        Assert.Contains("Redeploy the release", cut.Markup);
        // Only the alert's own close button may exist: nothing continues, skips, applies or retries.
        Assert.DoesNotContain(cut.FindAll("button"), b =>
            b.TextContent.Contains("Continue") || b.TextContent.Contains("Skip") ||
            b.TextContent.Contains("Apply") || b.TextContent.Contains("Retry"));
    }
}
