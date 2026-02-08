using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Data;
using BlazorDataOrchestrator.Core.Services;
using BlazorDataOrchestrator.JobCreatorTemplate.Components;
using BlazorDataOrchestrator.JobCreatorTemplate.Services;
using GitHub.Copilot.SDK;
using Radzen;
using IAIChatService = BlazorDataOrchestrator.Core.Services.IAIChatService;
using CopilotChatService = BlazorDataOrchestrator.JobCreatorTemplate.Services.CopilotChatService;

namespace BlazorDataOrchestrator.JobCreatorTemplate
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add Azure clients
            builder.AddAzureBlobServiceClient("blobs");
            builder.AddAzureTableServiceClient("tables");
            builder.AddAzureQueueServiceClient("queues");

            builder.AddSqlServerDbContext<ApplicationDbContext>("blazororchestratordb");

            // Add services to the container.
            builder.Services.AddHttpClient();
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddRadzenComponents();
            
            // Register Copilot Client as Singleton (one CLI process for the app)
            builder.Services.AddSingleton<CopilotClient>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var cliUrl = config.GetValue<string>("Copilot:CliUrl");
                var options = new CopilotClientOptions
                {
                    AutoStart = true,
                    AutoRestart = true,
                    UseStdio = true,
                    LogLevel = "info"
                };
                if (!string.IsNullOrEmpty(cliUrl))
                {
                    options.CliUrl = cliUrl;
                }
                return new CopilotClient(options);
            });

            // Register Copilot Chat Service for code assistance
            builder.Services.AddScoped<CopilotChatService>();
            builder.Services.AddScoped<IAIChatService>(sp => sp.GetRequiredService<CopilotChatService>());
            builder.Services.AddScoped<Radzen.IAIChatService>(sp => sp.GetRequiredService<CopilotChatService>());
            
            // Register NuGet Package Service
            builder.Services.AddScoped<NuGetPackageService>();
            
            // Register JobManager for package upload and job management
            builder.Services.AddScoped<JobManager>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var sqlConnectionString = config.GetConnectionString("blazororchestratordb") ?? "";
                var blobConnectionString = config.GetConnectionString("blobs") ?? "";
                var queueConnectionString = config.GetConnectionString("queues") ?? "";
                var tableConnectionString = config.GetConnectionString("tables") ?? "";
                return new JobManager(sqlConnectionString, blobConnectionString, queueConnectionString, tableConnectionString);
            });
            
            var app = builder.Build();

            // Start the CopilotClient when the app starts
            var copilotClient = app.Services.GetRequiredService<CopilotClient>();
            await copilotClient.StartAsync();

            // Ensure cleanup on shutdown
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() =>
            {
                copilotClient.StopAsync().GetAwaiter().GetResult();
            });

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            
            // Endpoint for downloading NuGet packages
            app.MapGet("/api/download-package", async (string path) =>
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return Results.NotFound("Package not found.");
                }

                var bytes = await File.ReadAllBytesAsync(path);
                var fileName = Path.GetFileName(path);
                
                return Results.File(bytes, "application/octet-stream", fileName);
            });
            
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            await app.RunAsync();
        }
    }
}
