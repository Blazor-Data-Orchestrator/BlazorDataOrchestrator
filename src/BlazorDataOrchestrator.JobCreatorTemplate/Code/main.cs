using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BlazorOrchestrator.Web.Data.Data;

namespace DataOrchestrator
{
    public class Program
    {
        public static async Task ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId)
        {
            // Parse connection string from appSettings JSON
            string connectionString = "";
            try
            {
                var settings = JsonSerializer.Deserialize<JsonElement>(appSettings);
                if (settings.TryGetProperty("ConnectionStrings", out var connStrings) &&
                    connStrings.TryGetProperty("DefaultConnection", out var defaultConn))
                {
                    connectionString = defaultConn.GetString();
                }
            }
            catch { }

            // Initialize database context for logging
            ApplicationDbContext dbContext = null;
            if (!string.IsNullOrEmpty(connectionString))
            {
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                optionsBuilder.UseSqlServer(connectionString);
                dbContext = new ApplicationDbContext(optionsBuilder.Options);
            }

            try
            {
                await LogProgress(dbContext, jobInstanceId, "Job started", "Info");
                Console.WriteLine($"Executing Job ID: {jobId}, Instance: {jobInstanceId}, Schedule: {jobScheduleId}, Agent: {jobAgentId}");

                // Fetch weather data for Los Angeles, CA
                await LogProgress(dbContext, jobInstanceId, "Fetching weather data for Los Angeles, CA", "Info");
                
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "BlazorDataOrchestrator/1.0");
                
                // Using wttr.in as a free weather API (weather.com requires API key)
                string weatherUrl = "https://wttr.in/Los+Angeles,CA?format=j1";
                
                try
                {
                    var response = await httpClient.GetStringAsync(weatherUrl);
                    var weatherData = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    // Extract current weather information
                    var currentCondition = weatherData.GetProperty("current_condition")[0];
                    string tempC = currentCondition.GetProperty("temp_C").GetString();
                    string tempF = currentCondition.GetProperty("temp_F").GetString();
                    string humidity = currentCondition.GetProperty("humidity").GetString();
                    string weatherDesc = currentCondition.GetProperty("weatherDesc")[0].GetProperty("value").GetString();

                    string weatherInfo = $"Los Angeles, CA - Temperature: {tempF}°F ({tempC}°C), Humidity: {humidity}%, Conditions: {weatherDesc}";
                    Console.WriteLine(weatherInfo);
                    await LogProgress(dbContext, jobInstanceId, weatherInfo, "Info");
                }
                catch (HttpRequestException ex)
                {
                    string errorMsg = $"Failed to fetch weather data: {ex.Message}";
                    Console.WriteLine(errorMsg);
                    await LogProgress(dbContext, jobInstanceId, errorMsg, "Warning");
                }

                await LogProgress(dbContext, jobInstanceId, "Job completed successfully", "Info");
            }
            catch (Exception ex)
            {
                string errorMsg = $"Job execution error: {ex.Message}";
                Console.WriteLine(errorMsg);
                await LogError(dbContext, jobInstanceId, errorMsg, ex.StackTrace);
                throw;
            }
            finally
            {
                dbContext?.Dispose();
            }
        }

        private static async Task LogProgress(ApplicationDbContext dbContext, int jobInstanceId, string message, string level)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
            
            if (dbContext != null && jobInstanceId > 0)
            {
                try
                {
                    var jobData = new JobDatum
                    {
                        JobId = (await dbContext.JobInstances
                            .Include(i => i.JobSchedule)
                            .FirstOrDefaultAsync(i => i.Id == jobInstanceId))?.JobSchedule?.JobId ?? 0,
                        JobFieldDescription = $"Log_{level}_{DateTime.UtcNow:yyyyMMddHHmmss}",
                        JobStringValue = message,
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = "JobExecutor"
                    };
                    
                    if (jobData.JobId > 0)
                    {
                        dbContext.JobData.Add(jobData);
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch { /* Fail silently if logging fails */ }
            }
        }

        private static async Task LogError(ApplicationDbContext dbContext, int jobInstanceId, string message, string stackTrace)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [ERROR] {message}");
            
            if (dbContext != null && jobInstanceId > 0)
            {
                try
                {
                    var instance = await dbContext.JobInstances
                        .Include(i => i.JobSchedule)
                        .FirstOrDefaultAsync(i => i.Id == jobInstanceId);
                    
                    if (instance != null)
                    {
                        instance.HasError = true;
                        instance.UpdatedDate = DateTime.UtcNow;
                        instance.UpdatedBy = "JobExecutor";
                        
                        var jobData = new JobDatum
                        {
                            JobId = instance.JobSchedule?.JobId ?? 0,
                            JobFieldDescription = $"Error_{DateTime.UtcNow:yyyyMMddHHmmss}",
                            JobStringValue = $"{message}\n{stackTrace}",
                            CreatedDate = DateTime.UtcNow,
                            CreatedBy = "JobExecutor"
                        };
                        
                        if (jobData.JobId > 0)
                        {
                            dbContext.JobData.Add(jobData);
                        }
                        
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch { /* Fail silently if logging fails */ }
            }
        }
    }
}
