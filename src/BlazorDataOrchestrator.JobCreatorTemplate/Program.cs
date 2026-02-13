using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Data;
using BlazorDataOrchestrator.Core.Services;
using BlazorDataOrchestrator.JobCreatorTemplate.Components;
using BlazorDataOrchestrator.JobCreatorTemplate.Services;
using Azure.Data.Tables;
using GitHub.Copilot.SDK;
using Radzen;
using IAIChatService = BlazorDataOrchestrator.Core.Services.IAIChatService;
using CopilotChatService = BlazorDataOrchestrator.JobCreatorTemplate.Services.CopilotChatService;
using CoreTimeDisplayService = BlazorDataOrchestrator.Core.Services.TimeDisplayService;

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

            // Register Settings service (uses Azure Table Storage)
            builder.Services.AddScoped<SettingsService>(sp =>
            {
                var tableServiceClient = sp.GetRequiredService<TableServiceClient>();
                return new SettingsService(tableServiceClient);
            });

            // Register shared TimeDisplayService from Core
            builder.Services.AddScoped<CoreTimeDisplayService>();
            
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
            
            // Register Copilot Health & Model services
            builder.Services.AddSingleton<CopilotHealthService>();
            builder.Services.AddSingleton<CopilotModelService>();
            
            // Register EmbeddedInstructionsProvider for AI instruction fallback
            builder.Services.AddSingleton<EmbeddedInstructionsProvider>();
            
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

            // Start the CopilotClient when the app starts (with health checks)
            var copilotClient = app.Services.GetRequiredService<CopilotClient>();
            var copilotHealth = app.Services.GetRequiredService<CopilotHealthService>();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            var cliInstalled = await copilotHealth.CheckCliInstalledAsync();

            if (!cliInstalled)
            {
                copilotHealth.SetStatus("Copilot CLI not found",
                    "Install the Copilot CLI and restart your computer.");
                logger.LogWarning("Copilot CLI not found — AI chat will be unavailable until the CLI is installed.");
            }
            else
            {
                // Check authentication before attempting to start
                var isAuthenticated = await copilotHealth.CheckAuthenticatedAsync();
                if (!isAuthenticated)
                {
                    logger.LogWarning("Copilot CLI is installed but does not appear to be authenticated. " +
                        "The SDK may still work if it handles authentication internally.");
                }

                try
                {
                    await copilotClient.StartAsync();
                    copilotHealth.SetStatus("Connected");
                    logger.LogInformation("CopilotClient started successfully.");
                }
                catch (Exception ex)
                {
                    copilotHealth.SetStatus("Connection failed", ex.Message);
                    logger.LogError(ex,
                        "CopilotClient.StartAsync() failed — AI chat will be unavailable. " +
                        "Ensure the Copilot CLI is authenticated (run 'copilot login' or set GITHUB_TOKEN).");
                }
            }

            // Log instruction file availability at startup
            var instructionsProvider = app.Services.GetRequiredService<EmbeddedInstructionsProvider>();
            var csharpInstructions = instructionsProvider.GetCSharpInstructions();
            var pythonInstructions = instructionsProvider.GetPythonInstructions();
            if (!string.IsNullOrWhiteSpace(csharpInstructions))
            {
                logger.LogInformation("Loaded C# instructions ({Lines} lines, {Chars} chars)",
                    csharpInstructions.Split('\n').Length, csharpInstructions.Length);
            }
            else
            {
                logger.LogWarning("C# instructions file is missing or empty");
            }
            if (!string.IsNullOrWhiteSpace(pythonInstructions))
            {
                logger.LogInformation("Loaded Python instructions ({Lines} lines, {Chars} chars)",
                    pythonInstructions.Split('\n').Length, pythonInstructions.Length);
            }
            else
            {
                logger.LogWarning("Python instructions file is missing or empty");
            }

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
