using System.Reflection;
using BlazorOrchestrator.Web.Components.Pages;
using BlazorOrchestrator.Web.Data.Data;
using BlazorOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

public class UpgradeAccessTests
{
    // ---------- Review #5: legacy Admin promotion ----------

    [Fact(DisplayName = "Review #5: before the role migration, an install without Admins promotes its existing users once")]
    [Trait("Category", "Security")]
    public async Task Bootstrap_BeforeMigration_PromotesExistingUsers()
    {
        await using var db = CreateDb();
        db.AspNetUsers.Add(NewUser("u1"));
        db.AspNetUsers.Add(NewUser("u2"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new AllowedUserService(db, NullLogger<AllowedUserService>.Instance);

        var hasAdmin = await service.BootstrapAdminRoleAsync(allowLegacyPromotion: true, TestContext.Current.CancellationToken);

        Assert.True(hasAdmin);
        Assert.Equal(2, await db.AspNetUsers.CountAsync(u => u.Roles.Any(r => r.NormalizedName == "ADMIN"), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Review #5: after the role migration, losing the last Admin never grants anyone Admin (fail closed)")]
    [Trait("Category", "Security")]
    public async Task Bootstrap_AfterMigration_FailsClosed()
    {
        await using var db = CreateDb();
        db.AspNetUsers.Add(NewUser("u1"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new AllowedUserService(db, NullLogger<AllowedUserService>.Instance);

        var hasAdmin = await service.BootstrapAdminRoleAsync(allowLegacyPromotion: false, TestContext.Current.CancellationToken);

        Assert.False(hasAdmin);
        Assert.False(await db.AspNetUsers.AnyAsync(u => u.Roles.Any(r => r.NormalizedName == "ADMIN"), TestContext.Current.CancellationToken));
        var auth = new AuthService(db);
        Assert.False(await auth.IsActiveAdminAsync("u1", TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "PR #47 HIGH-2: fresh install -> first Admin -> Admin removed -> restart never promotes ViewOnly users")]
    [Trait("Category", "Security")]
    public async Task FreshInstall_AdminRemoved_RestartDoesNotPromote()
    {
        await using var db = CreateTransactionalDb();
        var marker = new FakeMarker();
        var service = new AllowedUserService(db, NullLogger<AllowedUserService>.Instance);

        // Startup ran before the installer: no users yet, nothing to mark.
        Assert.False(await service.BootstrapAdminRoleAsync(allowLegacyPromotion: !await marker.IsCompletedAsync(), TestContext.Current.CancellationToken));
        Assert.False(marker.Completed);

        // The Install Wizard creates the first Admin and records the marker.
        Assert.True(await service.CreateFirstAdminAsync("boss", "boss@example.com", "Correct-Horse-1", marker, TestContext.Current.CancellationToken));
        Assert.True(marker.Completed);

        var viewer = NewUser("viewer");
        viewer.Roles.Add(await db.AspNetRoles.SingleAsync(r => r.NormalizedName == "VIEWONLY", TestContext.Current.CancellationToken));
        db.AspNetUsers.Add(viewer);
        var boss = await db.AspNetUsers.Include(u => u.Roles).SingleAsync(u => u.UserName == "boss", TestContext.Current.CancellationToken);
        boss.Roles.Clear();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Restart with the marker as recorded ...
        Assert.False(await service.BootstrapAdminRoleAsync(allowLegacyPromotion: !await marker.IsCompletedAsync(), TestContext.Current.CancellationToken));
        // ... and even if the marker were lost: the database is role-aware, so it is not a legacy install.
        Assert.False(await service.BootstrapAdminRoleAsync(allowLegacyPromotion: true, TestContext.Current.CancellationToken));

        Assert.False(await db.AspNetUsers.AnyAsync(u => u.Roles.Any(r => r.NormalizedName == "ADMIN"), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "PR #47 HIGH-2: the installer fails visibly and creates nothing when the marker cannot be written")]
    [Trait("Category", "Security")]
    public async Task CreateFirstAdmin_MarkerWriteFails_CreatesNothing()
    {
        await using var db = CreateTransactionalDb();
        var service = new AllowedUserService(db, NullLogger<AllowedUserService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateFirstAdminAsync("boss", "boss@example.com", "Correct-Horse-1", new FakeMarker { FailWrites = true }, TestContext.Current.CancellationToken));

        Assert.False(await db.AspNetUsers.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "PR #47 HIGH-2: a database whose users already have roles is not treated as legacy")]
    [Trait("Category", "Security")]
    public async Task Bootstrap_UsersWithRoles_IsNotLegacy()
    {
        await using var db = CreateDb();
        var viewer = NewUser("viewer");
        viewer.Roles.Add(new AspNetRole { Id = "v", Name = "ViewOnly", NormalizedName = "VIEWONLY" });
        db.AspNetUsers.Add(viewer);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new AllowedUserService(db, NullLogger<AllowedUserService>.Instance);

        Assert.False(await service.BootstrapAdminRoleAsync(allowLegacyPromotion: true, TestContext.Current.CancellationToken));
        Assert.False(await db.AspNetUsers.AnyAsync(u => u.Roles.Any(r => r.NormalizedName == "ADMIN"), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Review #5: an unreadable migration marker / schema version is an error, not 'not set'")]
    [Trait("Category", "Security")]
    public async Task StrictSettingsRead_FailsClosed()
    {
        // Nothing listens on port 1: every request fails without a 404.
        var client = new Azure.Data.Tables.TableServiceClient(
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:1/devstoreaccount1",
            new Azure.Data.Tables.TableClientOptions { Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(5) } });
        var settings = new BlazorDataOrchestrator.Core.Services.SettingsService(client);

        // The lenient read treats the outage as "not set" ...
        Assert.Null(await settings.GetAsync(AllowedUserService.AdminRoleMigrationSettingKey));
        // ... the reads that gate Admin promotion and the schema version throw instead.
        await Assert.ThrowsAnyAsync<Exception>(() => settings.GetStrictAsync(AllowedUserService.AdminRoleMigrationSettingKey, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(() => new BlazorOrchestrator.Web.Data.SettingsSchemaVersionStore(settings).GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Review #5: the Upgrade Wizard's Admin check fails closed for disabled, locked or unknown users")]
    [Trait("Category", "Security")]
    public async Task IsActiveAdmin_FailsClosed()
    {
        await using var db = CreateDb();
        var role = new AspNetRole { Id = "r", Name = "Admin", NormalizedName = "ADMIN" };
        var active = NewUser("active"); active.Roles.Add(role);
        var disabled = NewUser("disabled"); disabled.EmailConfirmed = false; disabled.Roles.Add(role);
        var locked = NewUser("locked"); locked.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); locked.Roles.Add(role);
        db.AspNetUsers.AddRange(active, disabled, locked);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var auth = new AuthService(db);

        Assert.True(await auth.IsActiveAdminAsync("active", TestContext.Current.CancellationToken));
        Assert.False(await auth.IsActiveAdminAsync("disabled", TestContext.Current.CancellationToken));
        Assert.False(await auth.IsActiveAdminAsync("locked", TestContext.Current.CancellationToken));
        Assert.False(await auth.IsActiveAdminAsync("nobody", TestContext.Current.CancellationToken));
        Assert.False(await auth.IsActiveAdminAsync("", TestContext.Current.CancellationToken));
    }

    // ---------- Review #1: leaving the wizard never reaches an unauthenticated dashboard ----------

    [Fact(DisplayName = "Review #1: the dashboard requires a signed-in Admin or ViewOnly user")]
    [Trait("Category", "Security")]
    public void Dashboard_RequiresAuthorization()
    {
        Assert.Empty(typeof(Home).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        var authorize = Assert.Single(typeof(Home).GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal("Admin,ViewOnly", authorize.Roles);
    }

    [Fact(DisplayName = "Review #1: the Upgrade Wizard has no skip path out of the wizard")]
    [Trait("Category", "Security")]
    public void UpgradeWizard_HasNoSkipHandler()
    {
        var methods = typeof(BlazorOrchestrator.Web.Components.Pages.InstallUpgrade.UpgradeWorkflow)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(m => m.Name);

        Assert.DoesNotContain("SkipUpgrade", methods);
    }

    // ---------- Housekeeping: /alive answers while setup or an upgrade is pending ----------

    [Theory(DisplayName = "Setup gate: health endpoints and setup pages bypass the redirect to /setup")]
    [Trait("Category", "Security")]
    [InlineData("/alive", true)]
    [InlineData("/health", true)]
    [InlineData("/setup", true)]
    [InlineData("/account/login", true)]
    [InlineData("/", false)]
    [InlineData("/admin/users", false)]
    [InlineData("/alivex", false)]
    public void SetupGate_ExemptPaths(string path, bool exempt)
    {
        Assert.Equal(exempt, SetupGate.IsExempt(new PathString(path), isStaticAsset: false));
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"access-{Guid.NewGuid():N}")
            .Options);

    // The installer uses a transaction; the in-memory provider ignores it.
    private static ApplicationDbContext CreateTransactionalDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"access-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FakeMarker : IAdminRoleMigrationMarker
    {
        public bool Completed { get; private set; }
        public bool FailWrites { get; init; }

        public Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default) => Task.FromResult(Completed);

        public Task MarkCompletedAsync(CancellationToken cancellationToken = default)
        {
            if (FailWrites) throw new InvalidOperationException("Simulated Table Storage outage.");
            Completed = true;
            return Task.CompletedTask;
        }
    }

    private static AspNetUser NewUser(string id) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        Email = $"{id}@example.com",
        NormalizedEmail = $"{id}@example.com".ToUpperInvariant(),
        EmailConfirmed = true
    };
}
