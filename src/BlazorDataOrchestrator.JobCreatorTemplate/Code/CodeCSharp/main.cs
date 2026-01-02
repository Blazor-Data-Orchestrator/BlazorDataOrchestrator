using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Data;

public class BlazorDataOrchestratorJob
{
    public static async Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId)
    { 
        var logs = new List<string>();

        // Parse connection strings from appSettings JSON
        string connectionString = "";
        string tableConnectionString = "";
        try
        {
            var settings = JsonSerializer.Deserialize<JsonElement>(appSettings);
            if (settings.TryGetProperty("ConnectionStrings", out var connStrings))
            {
                if (connStrings.TryGetProperty("blazororchestratordb", out var defaultConn))
                {
                    connectionString = defaultConn.GetString() ?? "";
                }
                if (connStrings.TryGetProperty("tables", out var tableConn))
                {
                    tableConnectionString = tableConn.GetString() ?? "";
                }
            }
        }
        catch { }

        // Initialize database context for logging
        ApplicationDbContext? dbContext = null;
        if (!string.IsNullOrEmpty(connectionString))
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(connectionString);
            dbContext = new ApplicationDbContext(optionsBuilder.Options);
        }

        try
        {
            // Get the jobId from the jobInstanceId if not provided (validates the relationship)
            if (jobId <= 0 && jobInstanceId > 0 && dbContext != null)
            {
                jobId = await JobManager.GetJobIdFromInstanceAsync(dbContext, jobInstanceId);
            }

            await JobManager.LogProgress(dbContext, jobInstanceId, "Job started", "Info", tableConnectionString);
            logs.Add("Job started");
            
            string execInfo = $"Executing Job ID: {jobId}, Instance: {jobInstanceId}, Schedule: {jobScheduleId}, Agent: {jobAgentId}";
            Console.WriteLine(execInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, execInfo, "Info", tableConnectionString);
            logs.Add(execInfo);

            string partitionKeyInfo = $"Log partition key: {jobId}-{jobInstanceId}";
            Console.WriteLine(partitionKeyInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, partitionKeyInfo, "Info", tableConnectionString);
            logs.Add(partitionKeyInfo);

            // Check for previous run time
            JobDatum? lastRunDatum = null;
            if (dbContext != null && jobId > 0)
            {
                lastRunDatum = await dbContext.JobData
                    .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == "Last Job Run Time");

                if (lastRunDatum != null && lastRunDatum.JobDateValue.HasValue)
                {
                    var localTime = lastRunDatum.JobDateValue.Value.ToLocalTime();
                    string prevRunMsg = $"Previous time the job was run: {localTime:MM/dd/yyyy hh:mm}{localTime.ToString("tt").ToLower()}";
                    Console.WriteLine(prevRunMsg);
                    await JobManager.LogProgress(dbContext, jobInstanceId, prevRunMsg, "Info", tableConnectionString);
                    logs.Add(prevRunMsg);
                }
            }

            // Fetch weather data for Los Angeles, CA
            await JobManager.LogProgress(dbContext, jobInstanceId, "Fetching weather data for Los Angeles, CA", "Info", tableConnectionString);
            logs.Add("Fetching weather data for Los Angeles, CA");
                
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
                string tempC = currentCondition.GetProperty("temp_C").GetString() ?? "";
                string tempF = currentCondition.GetProperty("temp_F").GetString() ?? "";
                string humidity = currentCondition.GetProperty("humidity").GetString() ?? "";
                string weatherDesc = currentCondition.GetProperty("weatherDesc")[0].GetProperty("value").GetString() ?? "";

                string weatherInfo = $"Los Angeles, CA - Temperature: {tempF}°F ({tempC}°C), Humidity: {humidity}%, Conditions: {weatherDesc}";
                Console.WriteLine(weatherInfo);
                await JobManager.LogProgress(dbContext, jobInstanceId, weatherInfo, "Info", tableConnectionString);
                logs.Add(weatherInfo);
            }
            catch (HttpRequestException ex)
            {
                string errorMsg = $"Failed to fetch weather data: {ex.Message}";
                Console.WriteLine(errorMsg);
                await JobManager.LogProgress(dbContext, jobInstanceId, errorMsg, "Warning", tableConnectionString);
                logs.Add(errorMsg);
            }

            await JobManager.LogProgress(dbContext, jobInstanceId, "Job completed successfully", "Info", tableConnectionString);
            logs.Add("Job completed successfully");
        }
        catch (Exception ex)
        {
            string errorMsg = $"Job execution error: {ex.Message}";
            Console.WriteLine(errorMsg);
            await JobManager.LogError(dbContext, jobInstanceId, errorMsg, ex.StackTrace, tableConnectionString);
            logs.Add(errorMsg);
            throw;
        }
        finally
        {
            // Update Last Job Run Time
            try
            {
                if (dbContext != null && jobId > 0)
                {
                    var runDatum = await dbContext.JobData
                        .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == "Last Job Run Time");

                    if (runDatum == null)
                    {
                        runDatum = new JobDatum
                        {
                            JobId = jobId,
                            JobFieldDescription = "Last Job Run Time",
                            CreatedBy = "System",
                            CreatedDate = DateTime.UtcNow
                        };
                        dbContext.JobData.Add(runDatum);
                    }
                    
                    runDatum.JobDateValue = DateTime.UtcNow;
                    runDatum.UpdatedBy = "System";
                    runDatum.UpdatedDate = DateTime.UtcNow;
                    
                    await dbContext.SaveChangesAsync();
                }
            }
            catch { }

            dbContext?.Dispose();
        }

        return logs;
    }
}