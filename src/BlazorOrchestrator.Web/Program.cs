using BlazorOrchestrator.Web.Components;
using BlazorOrchestrator.Web.Data;
using BlazorOrchestrator.Web.Data.Data;
using BlazorOrchestrator.Web.Services;
using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Radzen;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (health checks, resilience, OpenTelemetry)
builder.AddServiceDefaults();

// Add Azure clients using local connection strings from appsettings.json
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");
builder.AddAzureQueueServiceClient("queues");

// Add authentication with cookie scheme
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization(options =>
{
    // NOTE: Do NOT use FallbackPolicy with Blazor Server interactive mode.
    // FallbackPolicy applies to ALL endpoints including the /_blazor SignalR hub,
    // which blocks unauthenticated users from establishing the circuit — breaking
    // interactivity on [AllowAnonymous] pages (e.g., install wizard, login).
    // Component-level auth is handled by AuthorizeRouteView in Routes.razor.
});
builder.Services.AddCascadingAuthenticationState();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Check if a database connection string is available
var connectionString = builder.Configuration.GetConnectionString("blazororchestratordb");
var hasDatabaseConnection = !string.IsNullOrWhiteSpace(connectionString);

if (hasDatabaseConnection)
{
    // Use standard EF Core registration with connection string from appsettings.json
    // This ensures the project uses its own connection string, not one passed from Aspire
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        options.UseSqlServer(connectionString);
    });

    // Register IDbConnection using the local connection string from appsettings.json
    builder.Services.AddScoped<IDbConnection>(sp =>
    {
        // Use the connection string from local appsettings.json
        return new SqlConnection(connectionString);
    });

    // Run the database initialization script at startup
    builder.Services.AddHostedService(sp => new BackgroundInitializer(sp));
}
else
{
    // No database configured - register a placeholder DbContext that will fail gracefully
    // The Install Wizard will guide the user to configure the database
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        // Use an in-memory placeholder - actual connection will be configured via Install Wizard
        options.UseSqlServer("Server=localhost;Database=placeholder;Integrated Security=false;TrustServerCertificate=true;");
    });

    // Register IDbConnection that returns null - services should handle this gracefully
    builder.Services.AddScoped<IDbConnection>(sp => null!);
}

// Register authentication service
builder.Services.AddScoped<AuthService>();

// Register external authentication services
// NOTE: AuthenticationSettings is created as an instance so its flags can be
// populated during the pre-build external-auth configuration block below and
// observed by the registered singleton at runtime.
var authenticationSettings = new AuthenticationSettings();
builder.Services.AddSingleton(authenticationSettings);
builder.Services.AddScoped<AuthenticationSettingsService>();
builder.Services.AddScoped<ExternalLoginService>();
builder.Services.AddScoped<AllowedUserService>();

// Register custom services
builder.Services.AddScoped<ProjectCreatorService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<JobGroupService>();
builder.Services.AddScoped<JobQueueService>();
builder.Services.AddScoped<WebhookService>();

// Add controllers for webhook API
builder.Services.AddControllers();

// Register LLM Build Error Resolution services (in-memory stores — no external database)
builder.Services.AddSingleton<BuildErrorStore>();
builder.Services.AddSingleton<FixAttemptStore>();
builder.Services.AddSingleton<ContextGatherer>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<RootCauseClassifier>();
builder.Services.AddSingleton<BuildTelemetryReader>();
builder.Services.AddSingleton<LlmFixOrchestrator>(sp =>
{
    var orchestrator = new LlmFixOrchestrator(
        sp.GetRequiredService<BuildErrorStore>(),
        sp.GetRequiredService<FixAttemptStore>(),
        sp.GetRequiredService<ContextGatherer>(),
        sp.GetRequiredService<PromptBuilder>(),
        sp.GetRequiredService<RootCauseClassifier>(),
        sp.GetRequiredService<ILogger<LlmFixOrchestrator>>());
    orchestrator.SolutionRootPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
    return orchestrator;
});

// Register Settings service (uses Azure Table Storage)
builder.Services.AddScoped<SettingsService>(sp =>
{
    var tableServiceClient = sp.GetRequiredService<TableServiceClient>();
    return new SettingsService(tableServiceClient);
});

// Register settings and display services
builder.Services.AddScoped<AppSettingsService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var environment = sp.GetRequiredService<IWebHostEnvironment>();
    var settingsService = sp.GetRequiredService<SettingsService>();
    return new AppSettingsService(configuration, environment, settingsService);
});
builder.Services.AddScoped<BlazorOrchestrator.Web.Services.TimeDisplayService>();
builder.Services.AddScoped<WizardStateService>();

// Register AI Settings service (uses Azure Table Storage)
builder.Services.AddScoped<AISettingsService>(sp =>
{
    var tableServiceClient = sp.GetRequiredService<TableServiceClient>();
    return new AISettingsService(tableServiceClient);
});

// Register AI Model Cache service (fetches and caches provider models in Azure Table Storage)
builder.Services.AddScoped<AIModelCacheService>(sp =>
{
    var tableServiceClient = sp.GetRequiredService<TableServiceClient>();
    var logger = sp.GetRequiredService<ILogger<AIModelCacheService>>();
    return new AIModelCacheService(tableServiceClient, logger);
});

// Register AI Chat services
builder.Services.AddSingleton<IInstructionsProvider, EmbeddedInstructionsProvider>();
builder.Services.AddScoped<BlazorDataOrchestrator.Core.Services.IAIChatService, CodeAssistantChatService>();

// Register Core services (JobManager, JobStorageService, PackageProcessorService, CodeExecutorService)
builder.Services.AddScoped<JobStorageService>(sp =>
{
    var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
    return new JobStorageService(blobServiceClient);
});

builder.Services.AddScoped<PackageProcessorService>(sp =>
{
    var storageService = sp.GetRequiredService<JobStorageService>();
    return new PackageProcessorService(storageService);
});

builder.Services.AddScoped<CodeExecutorService>(sp =>
{
    var packageProcessor = sp.GetRequiredService<PackageProcessorService>();
    return new CodeExecutorService(packageProcessor);
});

builder.Services.AddScoped<JobManager>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var sqlConnectionString = config.GetConnectionString("blazororchestratordb") ?? "";
    var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
    var queueServiceClient = sp.GetRequiredService<QueueServiceClient>();
    var tableServiceClient = sp.GetRequiredService<TableServiceClient>();
    
    return new JobManager(sqlConnectionString, blobServiceClient, queueServiceClient, tableServiceClient);
});

// Register code editor services
builder.Services.AddSingleton<EditorFileStorageService>();
builder.Services.AddScoped<JobCodeEditorService>();

// Register CSharpCompilationService (uses Core NuGetResolverService for dotnet restore-based resolution)
builder.Services.AddScoped<CSharpCompilationService>();

builder.Services.AddScoped<PythonValidationService>();
builder.Services.AddScoped<WebNuGetPackageService>();

builder.Services.AddRadzenComponents();

// Configure external authentication providers from Azure Table Storage settings.
// This MUST run before builder.Build() because AddMicrosoftAccount/AddGoogle
// register services into IServiceCollection, which is sealed once Build() runs.
// We construct a dedicated TableServiceClient here (rather than calling
// builder.Services.BuildServiceProvider()) to avoid building the DI container
// twice, which causes disposed-singleton issues at runtime.
try
{
    var tablesConnectionString = builder.Configuration.GetConnectionString("tables");
    if (string.IsNullOrWhiteSpace(tablesConnectionString))
    {
        throw new InvalidOperationException("Connection string 'tables' is not configured.");
    }

    var tableServiceClient = new TableServiceClient(tablesConnectionString);
    var bootstrapSettingsService = new SettingsService(tableServiceClient);
    var bootstrapAuthConfigService = new AuthenticationSettingsService(bootstrapSettingsService);

    var microsoftConfig = await bootstrapAuthConfigService.GetMicrosoftConfigAsync();
    var googleConfig = await bootstrapAuthConfigService.GetGoogleConfigAsync();

    if (microsoftConfig.IsFullyConfigured)
    {
        authBuilder.AddMicrosoftAccount("Microsoft", options =>
        {
            options.ClientId = microsoftConfig.ClientId;
            options.ClientSecret = microsoftConfig.ClientSecret;
            // Use the v2.0 endpoints so we can sign in personal + work/school accounts
            options.AuthorizationEndpoint =
                "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
            options.TokenEndpoint =
                "https://login.microsoftonline.com/common/oauth2/v2.0/token";
            // v2.0 requires the openid scope; also request profile + email so we get
            // the user's name and email claims back.
            if (!options.Scope.Contains("openid")) options.Scope.Add("openid");
            if (!options.Scope.Contains("profile")) options.Scope.Add("profile");
            if (!options.Scope.Contains("email")) options.Scope.Add("email");
            options.SaveTokens = true;
            // Use the callback path that matches the external-login-callback route
            options.CallbackPath = "/signin-microsoft";
        });
        authenticationSettings.IsMicrosoftConfigured = true;
    }

    if (googleConfig.IsFullyConfigured)
    {
        authBuilder.AddGoogle("Google", options =>
        {
            options.ClientId = googleConfig.ClientId;
            options.ClientSecret = googleConfig.ClientSecret;
            options.SaveTokens = true;
        });
        authenticationSettings.IsGoogleConfigured = true;
    }
}
catch (Exception ex)
{
    // External auth configuration is optional — don't block startup
    Console.WriteLine($"Warning: Could not configure external authentication providers: {ex.Message}");
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map webhook API controllers
app.MapControllers();

app.MapDefaultEndpoints();

app.Run();

// Make the Program class available for tests
public partial class Program { }

public class BackgroundInitializer : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<BackgroundInitializer> _logger;
    private readonly IConfiguration _configuration;

    public BackgroundInitializer(IServiceProvider sp)
    {
        _sp = sp;
        _logger = sp.GetRequiredService<ILogger<BackgroundInitializer>>();
        _configuration = sp.GetRequiredService<IConfiguration>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Delay slightly to ensure SQL container is ready (Aspire wait-for helps, but this is defensive)
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await DatabaseInitializer.EnsureDatabaseAsync(_configuration, _logger);

            // Ensure the Admin role exists and that at least one enabled user is assigned to it.
            // Bootstraps the new Allowed Users administration screen.
            try
            {
                using var scope = _sp.CreateScope();
                var allowedUsers = scope.ServiceProvider.GetRequiredService<AllowedUserService>();
                await allowedUsers.BootstrapAdminRoleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Admin role bootstrap skipped.");
            }
        }
        catch (Exception ex)
        {
            // Don't let database initialization failure crash the app
            // The Home.razor page will detect the issue and show the install wizard
            _logger.LogWarning(ex, "Database initialization skipped - will be handled by install wizard.");
        }
    }
}
