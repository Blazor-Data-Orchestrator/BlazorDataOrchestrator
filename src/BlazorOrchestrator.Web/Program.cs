using BlazorOrchestrator.Web.Components;
using BlazorOrchestrator.Web.Data;
using BlazorOrchestrator.Web.Data.Data;
using BlazorOrchestrator.Web.Services;
using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Data.Tables;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Radzen;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Note: Not using Aspire service defaults - this project uses its own connection strings from appsettings.json
// builder.AddServiceDefaults();

// Add Azure clients using local connection strings from appsettings.json
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");
builder.AddAzureQueueServiceClient("queues");

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

// Register custom services
builder.Services.AddScoped<ProjectCreatorService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<JobGroupService>();
builder.Services.AddScoped<JobQueueService>();
builder.Services.AddScoped<WebhookService>();

// Add controllers for webhook API
builder.Services.AddControllers();

// Register settings and display services
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddScoped<TimeDisplayService>();
builder.Services.AddScoped<WizardStateService>();

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
    var blobConnectionString = config.GetConnectionString("blobs") ?? "";
    var queueConnectionString = config.GetConnectionString("queues") ?? "";
    var tableConnectionString = config.GetConnectionString("tables") ?? "";
    
    return new JobManager(sqlConnectionString, blobConnectionString, queueConnectionString, tableConnectionString);
});

// Register code editor services
builder.Services.AddSingleton<EditorFileStorageService>();
builder.Services.AddScoped<JobCodeEditorService>();

// Register CSharpCompilationService (uses Core NuGetResolverService for dotnet restore-based resolution)
builder.Services.AddScoped<CSharpCompilationService>();

builder.Services.AddScoped<PythonValidationService>();
builder.Services.AddScoped<WebNuGetPackageService>();

builder.Services.AddRadzenComponents();
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
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map webhook API controllers
app.MapControllers();

app.MapDefaultEndpoints();
// Note: Not using Aspire's MapDefaultEndpoints - this project runs standalone
// app.MapDefaultEndpoints();

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
        }
        catch (Exception ex)
        {
            // Don't let database initialization failure crash the app
            // The Home.razor page will detect the issue and show the install wizard
            _logger.LogWarning(ex, "Database initialization skipped - will be handled by install wizard.");
        }
    }
}
