using BlazorDataOrchestrator.Core.Data;
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
            
            // Register AI Chat Service for code assistance
            builder.Services.AddScoped<IAIChatService, CodeAssistantChatService>();
            
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
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
