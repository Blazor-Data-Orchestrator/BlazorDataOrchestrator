using BlazorOrchestrator.Web.Components;
using BlazorOrchestrator.Web.Data;
using Microsoft.Data.SqlClient;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add Azure clients
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");
builder.AddAzureQueueServiceClient("queues");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Aspire integrations - use the correct database name from AppHost
builder.AddSqlServerDbContext<ApplicationDbContext>("blazororchestratordb");

// Register IDbConnection before building the app
builder.Services.AddScoped<IDbConnection>(sp =>
    new SqlConnection(builder.Configuration.GetConnectionString("blazororchestratordb")));

// Run the database initialization script at startup
builder.Services.AddHostedService(sp => new BackgroundInitializer(sp));

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
        // Delay slightly to ensure SQL container is ready (Aspire wait-for helps, but this is defensive)
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        await DatabaseInitializer.EnsureDatabaseAsync(_configuration, _logger);
    }
}
