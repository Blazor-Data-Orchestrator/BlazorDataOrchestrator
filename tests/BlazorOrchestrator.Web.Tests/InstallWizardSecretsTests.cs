using Bunit;
using BlazorOrchestrator.Web.Components.Pages.InstallUpgrade;
using BlazorOrchestrator.Web.Data;
using BlazorOrchestrator.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Radzen;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>
/// Review #2: the anonymous Install Wizard must never render a connection string's secrets, and
/// environment-provided values (azd) are shown as provided, never copied into the wizard model
/// and never written to appsettings.json.
/// </summary>
public class InstallWizardSecretsTests : BunitContext
{
    private const string SqlSecret = "SqlSecret;Pass!1";
    private const string KeySecret = "c2VjcmV0LWtleQ==";

    private readonly string _contentRoot = Directory.CreateTempSubdirectory("bdo-install-").FullName;
    private readonly Dictionary<string, string?> _environment = new();

    public InstallWizardSecretsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddRadzenComponents();
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment(_contentRoot));
        Services.AddSingleton(new EnvironmentConnectionStrings(name => _environment.GetValueOrDefault(name)));
        File.WriteAllText(Path.Combine(_contentRoot, "appsettings.json"), "{ \"ConnectionStrings\": { } }");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_contentRoot, recursive: true); } catch { }
    }

    private void UseConfiguration(Dictionary<string, string?> values) =>
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact(DisplayName = "Review #2: the Review & Install summary never renders passwords, keys or SAS tokens")]
    [Trait("Category", "Security")]
    public void Summary_DoesNotRenderSecrets()
    {
        var model = new InstallationModel
        {
            DbConnectionString = $"Server=tcp:sql.example.net;Database=bdo;User Id=sa;Password=\"{SqlSecret}\"",
            BlobString = $"DefaultEndpointsProtocol=https;AccountName=acct;AccountKey={KeySecret};EndpointSuffix=core.windows.net",
            TableString = $"BlobEndpoint=https://acct.table.core.windows.net/;SharedAccessSignature=sv=1&sig={KeySecret}",
            QueueString = $"https://acct.queue.core.windows.net/?sig={KeySecret}"
        };

        var cut = Render<StepSummary>(parameters => parameters.Add(p => p.Model, model));

        Assert.DoesNotContain("SqlSecret", cut.Markup);
        Assert.DoesNotContain("Pass!1", cut.Markup);
        Assert.DoesNotContain(KeySecret, cut.Markup);
        Assert.DoesNotContain("c2VjcmV0", cut.Markup);
        Assert.Contains("Server: tcp:sql.example.net; Database: bdo", cut.Markup);
        Assert.Contains("Storage account: acct", cut.Markup);
    }

    [Fact(DisplayName = "Review #2: environment-provided values are shown as provided in the summary")]
    [Trait("Category", "Security")]
    public void Summary_ShowsEnvironmentValuesAsProvided()
    {
        var model = new InstallationModel
        {
            DbProvidedByEnvironment = true,
            BlobProvidedByEnvironment = true,
            TableProvidedByEnvironment = true,
            QueueProvidedByEnvironment = true
        };

        var cut = Render<StepSummary>(parameters => parameters.Add(p => p.Model, model));

        var occurrences = cut.Markup.Split(EnvironmentConnectionStrings.ProvidedByText).Length - 1;
        Assert.Equal(4, occurrences);
    }

    [Fact(DisplayName = "Review #2 / Install Wizard: an azd-provided database connection string is not copied, rendered or written")]
    [Trait("Category", "Security")]
    public void StepDatabase_EnvironmentConnectionString_IsNotCopiedRenderedOrWritten()
    {
        var provided = $"Server=tcp:sql.example.net;Database=bdo;Password=\"{SqlSecret}\"";
        _environment["ConnectionStrings__blazororchestratordb"] = provided;
        UseConfiguration(new() { ["ConnectionStrings:blazororchestratordb"] = provided });
        var model = new InstallationModel();

        var cut = Render<StepDatabase>(parameters => parameters.Add(p => p.Model, model));

        Assert.True(model.DbProvidedByEnvironment);
        Assert.Equal(string.Empty, model.DbConnectionString);
        Assert.Contains(EnvironmentConnectionStrings.ProvidedByText, cut.Markup);
        Assert.DoesNotContain("SqlSecret", cut.Markup);
        Assert.DoesNotContain("sql.example.net", cut.Markup);
        Assert.Empty(cut.FindAll("textarea"));
        Assert.DoesNotContain("blazororchestratordb\":", File.ReadAllText(Path.Combine(_contentRoot, "appsettings.json")));
    }

    [Fact(DisplayName = "Install Wizard: azd-provided storage connection strings are shown as provided, not copied")]
    [Trait("Category", "Security")]
    public void StepStorage_EnvironmentConnectionStrings_AreNotCopiedOrRendered()
    {
        _environment["ConnectionStrings__blobs"] = $"https://acct.blob.core.windows.net/?sig={KeySecret}";
        _environment["ConnectionStrings__tables"] = $"AccountName=acct;AccountKey={KeySecret}";
        _environment["ConnectionStrings__queues"] = "https://acct.queue.core.windows.net/";
        UseConfiguration(new());
        var model = new InstallationModel();

        var cut = Render<StepStorage>(parameters => parameters.Add(p => p.Model, model));

        Assert.True(model.BlobProvidedByEnvironment && model.TableProvidedByEnvironment && model.QueueProvidedByEnvironment);
        Assert.Equal(string.Empty, model.BlobString + model.TableString + model.QueueString);
        Assert.DoesNotContain(KeySecret, cut.Markup);
        Assert.Contains(EnvironmentConnectionStrings.ProvidedByText, cut.Markup);
        Assert.Empty(cut.FindAll("input[type=text]"));
    }

    [Fact(DisplayName = "Install Wizard: no environment values means the manual entry is offered as before")]
    [Trait("Category", "Security")]
    public void StepDatabase_WithoutEnvironmentValue_OffersManualEntry()
    {
        UseConfiguration(new());
        var model = new InstallationModel();

        var cut = Render<StepDatabase>(parameters => parameters.Add(p => p.Model, model));

        Assert.False(model.DbProvidedByEnvironment);
        Assert.NotEmpty(cut.FindAll("textarea"));
        Assert.DoesNotContain(EnvironmentConnectionStrings.ProvidedByText, cut.Markup);
    }

    private sealed class FakeWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "BlazorOrchestrator.Web";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = Environments.Production;
    }
}
