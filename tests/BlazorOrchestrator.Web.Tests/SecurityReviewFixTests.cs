using System.IO.Compression;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using BlazorDataOrchestrator.Core.Configuration;
using BlazorDataOrchestrator.Core.Models;
using BlazorOrchestrator.Web.Components.Pages.Admin;
using BlazorOrchestrator.Web.Controllers;
using BlazorOrchestrator.Web.Data.Data;
using BlazorOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>Regression tests for the Critical and High findings in docs/SecurityReview.md.</summary>
public class SecurityReviewFixTests
{
    private static readonly ReservedConnectionStrings LiveReserved = new(
        Blobs: "DefaultEndpointsProtocol=https;AccountName=live;AccountKey=bGl2ZS1ibG9i",
        Queues: "DefaultEndpointsProtocol=https;AccountName=live;AccountKey=bGl2ZS1xdWV1ZQ==",
        Tables: "DefaultEndpointsProtocol=https;AccountName=live;AccountKey=bGl2ZS10YWJsZQ==",
        BlazorOrchestratorDb: "Server=tcp:live.database.windows.net;Database=prod;User ID=admin;Password=LiveSecret!1");

    // ---------- SEC-001 ----------

    [Fact(DisplayName = "SEC-001 T-01/T-02: package download requires the Admin role")]
    [Trait("Category", "Security")]
    public void JobPackageController_RequiresAdminRole()
    {
        var type = typeof(JobPackageController);

        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.Empty(type.GetMethods().SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)));
        var authorize = Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact(DisplayName = "SEC-001 T-03: packages built by the web editor carry blank reserved connection strings")]
    [Trait("Category", "Security")]
    public async Task WebPackage_BlanksReservedConnectionStrings()
    {
        var editor = new JobCodeEditorService(null!, null!, NullLogger<JobCodeEditorService>.Instance, new FixedReservedProvider(LiveReserved));
        var service = new WebNuGetPackageService(NullLogger<WebNuGetPackageService>.Instance, null!, null!, editor);

        // The base file arrives with live values, as an older editor buffer would hold them;
        // the environment overlays fall back to defaults that the editor fills from the host.
        var model = new JobCodeModel
        {
            Language = "csharp",
            MainCode = "public class BlazorDataOrchestratorJob { }",
            AppSettings = editor.GetDefaultAppSettings()
        };
        Assert.Contains("LiveSecret!1", model.AppSettings);

        var (stream, _, _) = await service.CreatePackageAsync(model, jobId: 7);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var settingsEntries = archive.Entries
            .Where(e => Path.GetFileName(e.FullName).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(4, settingsEntries.Count);

        foreach (var entry in settingsEntries)
        {
            using var reader = new StreamReader(entry.Open());
            var json = JsonNode.Parse(await reader.ReadToEndAsync())!;
            foreach (var key in ReservedConnectionStrings.ReservedKeys)
            {
                Assert.Equal("", json["ConnectionStrings"]![key]!.GetValue<string>());
            }
        }
    }

    // ---------- SEC-003 ----------

    [Fact(DisplayName = "SEC-003 T-06: /admin requires the Admin role")]
    [Trait("Category", "Security")]
    public void AdminPage_RequiresAdminRole()
    {
        var authorize = Assert.Single(typeof(AdminHome).GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact(DisplayName = "SEC-003 T-08: a stored AI key is not sent to a changed endpoint")]
    [Trait("Category", "Security")]
    public void KeyGuard_BlocksStoredKeyOnChangedEndpoint()
    {
        var guard = new AIKeyEndpointGuard();
        var provider = new AIProviderSettings { ApiKey = "stored-key", Endpoint = "https://contoso.openai.azure.com/" };
        guard.TrustSaved(provider);

        Assert.True(guard.CanSendKey(AIServiceType.AzureOpenAI, provider));

        provider.Endpoint = "https://CONTOSO.openai.azure.com";
        Assert.True(guard.CanSendKey(AIServiceType.AzureOpenAI, provider));

        provider.Endpoint = "https://attacker.example/";
        Assert.False(guard.CanSendKey(AIServiceType.AzureOpenAI, provider));
        Assert.False(guard.CanSendKey(AIServiceType.AzureAIFoundry, provider));

        // Re-entering the key is an explicit choice to use it with the new endpoint.
        provider.ApiKey = "typed-key";
        guard.TrustUserEnteredKey("typed-key");
        Assert.True(guard.CanSendKey(AIServiceType.AzureOpenAI, provider));
    }

    [Fact(DisplayName = "SEC-003: fixed-host providers are not restricted by the key guard")]
    [Trait("Category", "Security")]
    public void KeyGuard_IgnoresFixedHostProviders()
    {
        var guard = new AIKeyEndpointGuard();
        var provider = new AIProviderSettings { ApiKey = "sk-stored", Endpoint = "https://anything.example" };

        Assert.True(guard.CanSendKey(AIServiceType.OpenAI, provider));
        Assert.True(guard.CanSendKey(AIServiceType.Anthropic, provider));
    }

    // ---------- SEC-004 ----------

    [Theory(DisplayName = "SEC-004: only provider-verified emails can link an external login")]
    [Trait("Category", "Security")]
    [InlineData("Microsoft", null, "admin@victim.com", null, null)]
    [InlineData("Microsoft", "attacker@attacker.onmicrosoft.com", "admin@victim.com", null, "attacker@attacker.onmicrosoft.com")]
    [InlineData("Microsoft", "admin_victim.com#EXT#@attacker.onmicrosoft.com", "admin@victim.com", null, null)]
    [InlineData("Google", null, "user@gmail.com", "true", "user@gmail.com")]
    [InlineData("Google", null, "user@gmail.com", "false", null)]
    [InlineData("Google", null, "user@gmail.com", null, null)]
    [InlineData("Other", null, "user@x.com", "true", null)]
    public void GetLinkableEmail_UsesOnlyVerifiedIdentifiers(string provider, string? upn, string? mail, string? verified, string? expected)
    {
        var claims = new List<Claim>();
        if (upn != null) claims.Add(new Claim(ExternalLoginService.MicrosoftUpnClaimType, upn));
        if (mail != null) claims.Add(new Claim(ClaimTypes.Email, mail));
        if (verified != null) claims.Add(new Claim(ExternalLoginService.GoogleEmailVerifiedClaimType, verified));

        Assert.Equal(expected, ExternalLoginService.GetLinkableEmail(provider, claims));
    }

    [Fact(DisplayName = "SEC-004 T-09: an unlinked login with a spoofed mail claim is rejected and not linked")]
    [Trait("Category", "Security")]
    public async Task FindAndLinkUser_SpoofedMail_IsRejected()
    {
        await using var db = CreateDb();
        db.AspNetUsers.Add(NewUser("admin-id", "admin@victim.com"));
        await db.SaveChangesAsync();
        var service = new ExternalLoginService(db);

        var spoofedClaims = new[] { new Claim(ClaimTypes.Email, "admin@victim.com") };
        var linkable = ExternalLoginService.GetLinkableEmail("Microsoft", spoofedClaims);
        var user = await service.FindAndLinkUserAsync("Microsoft", "attacker-key", linkable, "Attacker");

        Assert.Null(user);
        Assert.Empty(db.AspNetUserLogins);
    }

    [Fact(DisplayName = "SEC-004: a verified identifier links once, then the link is reused")]
    [Trait("Category", "Security")]
    public async Task FindAndLinkUser_VerifiedUpn_LinksAndReuses()
    {
        await using var db = CreateDb();
        db.AspNetUsers.Add(NewUser("user-id", "user@contoso.com"));
        await db.SaveChangesAsync();
        var service = new ExternalLoginService(db);

        var linked = await service.FindAndLinkUserAsync("Microsoft", "oid-1", "user@contoso.com", "User");
        Assert.Equal("user-id", linked?.Id);
        Assert.Single(db.AspNetUserLogins);

        var again = await service.FindAndLinkUserAsync("Microsoft", "oid-1", linkableEmail: null, "User");
        Assert.Equal("user-id", again?.Id);
    }

    // ---------- SEC-005 ----------

    [Theory(DisplayName = "SEC-005 T-11: the anonymous setup wizard fails closed once installed")]
    [Trait("Category", "Security")]
    [InlineData("INSTALL", InstallMarkerState.Installed, false, SetupAccessPolicy.Unavailable)]
    [InlineData("INSTALL", InstallMarkerState.Unknown, false, SetupAccessPolicy.Unavailable)]
    [InlineData("CreateAdministrator", InstallMarkerState.Installed, false, SetupAccessPolicy.Unavailable)]
    [InlineData("RUNSCRIPTS", InstallMarkerState.Unknown, false, SetupAccessPolicy.Unavailable)]
    [InlineData("INSTALL", InstallMarkerState.NotInstalled, false, "INSTALL")]
    [InlineData("CreateAdministrator", InstallMarkerState.NotInstalled, false, "CreateAdministrator")]
    [InlineData("INSTALL", InstallMarkerState.Installed, true, "INSTALL")]
    [InlineData("UPGRADE", InstallMarkerState.Installed, false, "UPGRADE")]
    public void SetupAccessPolicy_Resolve(string detected, InstallMarkerState marker, bool isAdmin, string expected)
    {
        Assert.Equal(expected, SetupAccessPolicy.Resolve(detected, marker, isAdmin));
    }

    // ---------- helpers ----------

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"sec-{Guid.NewGuid():N}")
            .Options);

    private static AspNetUser NewUser(string id, string email) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true
    };

    private sealed class FixedReservedProvider(ReservedConnectionStrings value) : IReservedConnectionStringProvider
    {
        public ReservedConnectionStrings Get() => value;
    }
}
