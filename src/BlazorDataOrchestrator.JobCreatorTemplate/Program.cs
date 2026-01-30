using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Data;
using BlazorDataOrchestrator.Core.Services;
using BlazorDataOrchestrator.JobCreatorTemplate.Components;
using BlazorDataOrchestrator.JobCreatorTemplate.Services;
using Radzen;

namespace BlazorDataOrchestrator.JobCreatorTemplate
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add Azure clients
            builder.AddAzureBlobServiceClient("blobs");
            builder.AddAzureTableServiceClient("tables");
            builder.AddAzureQueueServiceClient("queues");

            builder.AddSqlServerDbContext<ApplicationDbContext>("blazororchestratordb");

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddRadzenComponents();
            
            // Register AI Settings Service
            builder.Services.AddScoped<AISettingsService>();
            
            // Register AI Chat Service for code assistance
            builder.Services.AddScoped<CodeAssistantChatService>();
            builder.Services.AddScoped<IAIChatService>(sp => sp.GetRequiredService<CodeAssistantChatService>());
            
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

            app.Run();
        }
    }
}
