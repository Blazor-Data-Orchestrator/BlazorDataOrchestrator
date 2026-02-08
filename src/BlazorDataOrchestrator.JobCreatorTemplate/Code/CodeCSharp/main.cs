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
    public static async Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId, string webAPIParameter)
    {
        // List to collect log messages
        var lsLogs = new List<string>();

        // Initialize Connection strings
        string connectionString = "";
        string tableConnectionString = "";

        try
        {
            // Deserialize appSettings to extract connection strings
            var settings = JsonSerializer.Deserialize<JsonElement>(appSettings);

            // Extract connection strings
            if (settings.TryGetProperty("ConnectionStrings", out var connStrings))
            {
                // Get specific connection strings
                if (connStrings.TryGetProperty("blazororchestratordb", out var defaultConn))
                {
                    // Primary database connection string
                    connectionString = defaultConn.GetString() ?? "";
                }

                // Table storage connection string
                if (connStrings.TryGetProperty("tables", out var tableConn))
                {
                    // Table storage connection string
                    tableConnectionString = tableConn.GetString() ?? "";
                }
            }
        }
        catch { }

        // Initialize database context for logging
        ApplicationDbContext? dbContext = null;

        // Create DbContext if connection string is available
        if (!string.IsNullOrEmpty(connectionString))
        {
            // Set up DbContext options
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            // Use SQL Server provider
            optionsBuilder.UseSqlServer(connectionString);

            // Instantiate the DbContext
            dbContext = new ApplicationDbContext(optionsBuilder.Options);
        }

        try
        {
            // Get the jobId from the jobInstanceId if not provided (validates the relationship)
            if (jobId <= 0 && jobInstanceId > 0 && dbContext != null)
            {
                // Fetch jobId using JobManager utility
                jobId = await JobManager.GetJobIdFromInstanceAsync(dbContext, jobInstanceId);
            }

            // Log job start
            await JobManager.LogProgress(dbContext, jobInstanceId, "Job started", "Info", tableConnectionString);
            lsLogs.Add("Job started");

            // Log execution details
            string execInfo = $"Executing Job ID: {jobId}, Instance: {jobInstanceId}, Schedule: {jobScheduleId}, Agent: {jobAgentId}";
            Console.WriteLine(execInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, execInfo, "Info", tableConnectionString);
            lsLogs.Add(execInfo);

            // Log partition key info
            string partitionKeyInfo = $"Log partition key: {jobId}-{jobInstanceId}";
            Console.WriteLine(partitionKeyInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, partitionKeyInfo, "Info", tableConnectionString);
            lsLogs.Add(partitionKeyInfo);

            // Check for previous run time
            JobDatum? lastRunDatum = null;
            if (dbContext != null && jobId > 0)
            {
                // Retrieve the last run time from JobData
                lastRunDatum = await dbContext.JobData
                    .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == "Last Job Run Time");

                // Log previous run time if available
                if (lastRunDatum != null && lastRunDatum.JobDateValue.HasValue)
                {
                    // Convert to local time for logging
                    var localTime = lastRunDatum.JobDateValue.Value.ToLocalTime();

                    // Log previous run time
                    string prevRunMsg = $"Previous time the job was run: {localTime:MM/dd/yyyy hh:mm}{localTime.ToString("tt").ToLower()}";
                    Console.WriteLine(prevRunMsg);
                    await JobManager.LogProgress(dbContext, jobInstanceId, prevRunMsg, "Info", tableConnectionString);
                    lsLogs.Add(prevRunMsg);
                }
            }

            string WeatherAPIParam = "Los+Angeles,CA";

            // If webAPIParameter is passed, use it to fetch weather data
            if (!string.IsNullOrEmpty(webAPIParameter))
            {
                WeatherAPIParam = webAPIParameter.Replace(" ", "+");
            }

            // Fetch weather data for webAPIParameter
            await JobManager.LogProgress(dbContext, jobInstanceId, $"Fetching weather data for {webAPIParameter}", "Info", tableConnectionString);
            lsLogs.Add($"Fetching weather data for {webAPIParameter}");

            // Set up HTTP client                
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BlazorDataOrchestrator/1.0");

            // Using wttr.in as a free weather API 
            string weatherUrl = $"https://wttr.in/{WeatherAPIParam}?format=j1";

            try
            {
                // Make the HTTP GET request
                var response = await httpClient.GetStringAsync(weatherUrl);

                // Parse the JSON response
                var weatherData = JsonSerializer.Deserialize<JsonElement>(response);

                // Extract current weather information
                var currentCondition = weatherData.GetProperty("current_condition")[0];
                string tempC = currentCondition.GetProperty("temp_C").GetString() ?? "";
                string tempF = currentCondition.GetProperty("temp_F").GetString() ?? "";
                string humidity = currentCondition.GetProperty("humidity").GetString() ?? "";
                string weatherDesc = currentCondition.GetProperty("weatherDesc")[0].GetProperty("value").GetString() ?? "";

                // Log the weather information
                string weatherInfo = $"Los Angeles, CA - Temperature: {tempF}°F ({tempC}°C), Humidity: {humidity}%, Conditions: {weatherDesc}";
                Console.WriteLine(weatherInfo);
                await JobManager.LogProgress(dbContext, jobInstanceId, weatherInfo, "Info", tableConnectionString);
                lsLogs.Add(weatherInfo);
            }
            catch (HttpRequestException ex)
            {
                string errorMsg = $"Failed to fetch weather data: {ex.Message}";
                Console.WriteLine(errorMsg);
                await JobManager.LogProgress(dbContext, jobInstanceId, errorMsg, "Warning", tableConnectionString);
                lsLogs.Add(errorMsg);
            }

            // Job completed successfully
            await JobManager.LogProgress(dbContext, jobInstanceId, "Job completed successfully!", "Info", tableConnectionString);
            lsLogs.Add("Job completed successfully!");
        }
        catch (Exception ex)
        {
            string errorMsg = $"Job execution error: {ex.Message}";
            Console.WriteLine(errorMsg);
            await JobManager.LogError(dbContext, jobInstanceId, errorMsg, ex.StackTrace, tableConnectionString);
            lsLogs.Add(errorMsg);
            throw;
        }
        finally
        {
            // Update Last Job Run Time
            try
            {
                // Update the last run time in JobData
                if (dbContext != null && jobId > 0)
                {
                    // Retrieve existing datum
                    var runDatum = await dbContext.JobData
                        .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == "Last Job Run Time");

                    // Create new datum if it doesn't exist
                    if (runDatum == null)
                    {
                        // Create new JobDatum
                        runDatum = new JobDatum
                        {
                            JobId = jobId,
                            JobFieldDescription = "Last Job Run Time",
                            CreatedBy = "System",
                            CreatedDate = DateTime.UtcNow
                        };

                        // Add to DbContext
                        dbContext.JobData.Add(runDatum);
                    }

                    // Update the date value
                    runDatum.JobDateValue = DateTime.UtcNow;
                    runDatum.UpdatedBy = "System";
                    runDatum.UpdatedDate = DateTime.UtcNow;

                    // Save changes to the database
                    await dbContext.SaveChangesAsync();
                }
            }
            catch { }

            dbContext?.Dispose();
        }

        // Return the collected logs
        return lsLogs;
    }
}