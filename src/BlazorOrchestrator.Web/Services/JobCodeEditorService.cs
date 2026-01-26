using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using BlazorDataOrchestrator.Core;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for managing job code editing in the web application.
/// Handles code loading, saving, and template generation.
/// </summary>
public class JobCodeEditorService
{
    private readonly JobManager _jobManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JobCodeEditorService> _logger;

    // Default code templates
    private const string DefaultCSharpTemplate = @"using System;
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
        var colLogs = new List<string>();

        // Initialize Connection strings
        string connectionString = """";
        string tableConnectionString = """";

        try
        {
            // Deserialize appSettings to extract connection strings
            var settings = JsonSerializer.Deserialize<JsonElement>(appSettings);

            // Extract connection strings
            if (settings.TryGetProperty(""ConnectionStrings"", out var connStrings))
            {
                // Get specific connection strings
                if (connStrings.TryGetProperty(""blazororchestratordb"", out var defaultConn))
                {
                    // Primary database connection string
                    connectionString = defaultConn.GetString() ?? """";
                }

                // Table storage connection string
                if (connStrings.TryGetProperty(""tables"", out var tableConn))
                {
                    // Table storage connection string
                    tableConnectionString = tableConn.GetString() ?? """";
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
            await JobManager.LogProgress(dbContext, jobInstanceId, ""Job started"", ""Info"", tableConnectionString);
            colLogs.Add(""Job started"");

            // Log execution details
            string execInfo = $""Executing Job ID: {jobId}, Instance: {jobInstanceId}, Schedule: {jobScheduleId}, Agent: {jobAgentId}"";
            Console.WriteLine(execInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, execInfo, ""Info"", tableConnectionString);
            colLogs.Add(execInfo);

            // Log partition key info
            string partitionKeyInfo = $""Log partition key: {jobId}-{jobInstanceId}"";
            Console.WriteLine(partitionKeyInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, partitionKeyInfo, ""Info"", tableConnectionString);
            colLogs.Add(partitionKeyInfo);

            // Check for previous run time
            JobDatum? lastRunDatum = null;
            if (dbContext != null && jobId > 0)
            {
                // Retrieve the last run time from JobData
                lastRunDatum = await dbContext.JobData
                    .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == ""Last Job Run Time"");

                // Log previous run time if available
                if (lastRunDatum != null && lastRunDatum.JobDateValue.HasValue)
                {
                    // Convert to local time for logging
                    var localTime = lastRunDatum.JobDateValue.Value.ToLocalTime();

                    // Log previous run time
                    string prevRunMsg = $""Previous time the job was run: {localTime:MM/dd/yyyy hh:mm}{localTime.ToString(""tt"").ToLower()}"";
                    Console.WriteLine(prevRunMsg);
                    await JobManager.LogProgress(dbContext, jobInstanceId, prevRunMsg, ""Info"", tableConnectionString);
                    colLogs.Add(prevRunMsg);
                }
            }

            string WeatherAPIParam = ""Los+Angeles,CA"";

            // If webAPIParameter is passed, use it to fetch weather data
            if (!string.IsNullOrEmpty(webAPIParameter))
            {
                WeatherAPIParam = webAPIParameter.Replace("" "", ""+"");
            }

            // Fetch weather data for webAPIParameter
            await JobManager.LogProgress(dbContext, jobInstanceId, $""Fetching weather data for {webAPIParameter}"", ""Info"", tableConnectionString);
            colLogs.Add($""Fetching weather data for {webAPIParameter}"");

            // Set up HTTP client                
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add(""User-Agent"", ""BlazorDataOrchestrator/1.0"");

            // Using wttr.in as a free weather API 
            string weatherUrl = $""https://wttr.in/{WeatherAPIParam}?format=j1"";

            try
            {
                // Make the HTTP GET request
                var response = await httpClient.GetStringAsync(weatherUrl);

                // Parse the JSON response
                var weatherData = JsonSerializer.Deserialize<JsonElement>(response);

                // Extract current weather information
                var currentCondition = weatherData.GetProperty(""current_condition"")[0];
                string tempC = currentCondition.GetProperty(""temp_C"").GetString() ?? """";
                string tempF = currentCondition.GetProperty(""temp_F"").GetString() ?? """";
                string humidity = currentCondition.GetProperty(""humidity"").GetString() ?? """";
                string weatherDesc = currentCondition.GetProperty(""weatherDesc"")[0].GetProperty(""value"").GetString() ?? """";

                // Log the weather information
                string weatherInfo = $""Los Angeles, CA - Temperature: {tempF}°F ({tempC}°C), Humidity: {humidity}%, Conditions: {weatherDesc}"";
                Console.WriteLine(weatherInfo);
                await JobManager.LogProgress(dbContext, jobInstanceId, weatherInfo, ""Info"", tableConnectionString);
                colLogs.Add(weatherInfo);
            }
            catch (HttpRequestException ex)
            {
                string errorMsg = $""Failed to fetch weather data: {ex.Message}"";
                Console.WriteLine(errorMsg);
                await JobManager.LogProgress(dbContext, jobInstanceId, errorMsg, ""Warning"", tableConnectionString);
                colLogs.Add(errorMsg);
            }

            // Job completed successfully
            await JobManager.LogProgress(dbContext, jobInstanceId, ""Job completed successfully!"", ""Info"", tableConnectionString);
            colLogs.Add(""Job completed successfully!"");
        }
        catch (Exception ex)
        {
            string errorMsg = $""Job execution error: {ex.Message}"";
            Console.WriteLine(errorMsg);
            await JobManager.LogError(dbContext, jobInstanceId, errorMsg, ex.StackTrace, tableConnectionString);
            colLogs.Add(errorMsg);
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
                        .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == ""Last Job Run Time"");

                    // Create new datum if it doesn't exist
                    if (runDatum == null)
                    {
                        // Create new JobDatum
                        runDatum = new JobDatum
                        {
                            JobId = jobId,
                            JobFieldDescription = ""Last Job Run Time"",
                            CreatedBy = ""System"",
                            CreatedDate = DateTime.UtcNow
                        };

                        // Add to DbContext
                        dbContext.JobData.Add(runDatum);
                    }

                    // Update the date value
                    runDatum.JobDateValue = DateTime.UtcNow;
                    runDatum.UpdatedBy = ""System"";
                    runDatum.UpdatedDate = DateTime.UtcNow;

                    // Save changes to the database
                    await dbContext.SaveChangesAsync();
                }
            }
            catch { }

            dbContext?.Dispose();
        }

        // Return the collected logs
        return colLogs;
    }
}";

    private const string DefaultPythonTemplate = @"import os
import json
import sys
import urllib.request
import urllib.error
from datetime import datetime
from typing import Optional
import uuid

# Database connection (requires pyodbc for SQL Server)
try:
    import pyodbc
    HAS_PYODBC = True
except ImportError:
    HAS_PYODBC = False

# Azure Table Storage (requires azure-data-tables)
# Ensure you have installed the dependencies listed in requirements.txt
try:
    from azure.data.tables import TableServiceClient, TableEntity
    HAS_AZURE_TABLES = True
except ImportError:
    HAS_AZURE_TABLES = False


class JobLogger:
    """"""Handles logging progress and errors to the database and Azure Table Storage.
    Logs are partitioned by '{JobId}-{JobInstanceId}' for efficient querying.""""""
    
    def __init__(self, connection_string: str, job_instance_id: int, table_connection_string: str = None):
        self.connection_string = connection_string
        self.job_instance_id = job_instance_id
        self.table_connection_string = table_connection_string
        self.connection = None
        self.job_id = 0
        self.table_client = None
        
        if HAS_PYODBC and connection_string:
            try:
                # Fix for ODBC requiring 'yes'/'no' instead of 'true'/'false'
                connection_string = connection_string.replace(""TrustServerCertificate=true"", ""TrustServerCertificate=yes"")
                connection_string = connection_string.replace(""TrustServerCertificate=True"", ""TrustServerCertificate=yes"")
                connection_string = connection_string.replace(""TrustServerCertificate=false"", ""TrustServerCertificate=no"")
                connection_string = connection_string.replace(""TrustServerCertificate=False"", ""TrustServerCertificate=no"")
                
                connection_string = connection_string.replace(""Encrypt=true"", ""Encrypt=yes"")
                connection_string = connection_string.replace(""Encrypt=True"", ""Encrypt=yes"")
                connection_string = connection_string.replace(""Encrypt=false"", ""Encrypt=no"")
                connection_string = connection_string.replace(""Encrypt=False"", ""Encrypt=no"")

                # Fix for ODBC using UID/PWD instead of User ID/Password
                connection_string = connection_string.replace(""User ID="", ""UID="")
                connection_string = connection_string.replace(""User Id="", ""UID="")
                connection_string = connection_string.replace(""Password="", ""PWD="")

                # Ensure driver is specified in connection string
                if ""Driver={"" not in connection_string:
                    drivers = pyodbc.drivers()
                    if ""ODBC Driver 17 for SQL Server"" in drivers:
                        connection_string += "";Driver={ODBC Driver 17 for SQL Server};""
                    elif ""ODBC Driver 18 for SQL Server"" in drivers:
                        connection_string += "";Driver={ODBC Driver 18 for SQL Server};TrustServerCertificate=yes;""
                    elif ""SQL Server"" in drivers:
                        connection_string += "";Driver={SQL Server};""

                self.connection = pyodbc.connect(connection_string)
                # Get job_id from job_instance_id
                self.job_id = self._get_job_id_from_instance()
            except Exception as e:
                print(f""Warning: Could not connect to database: {e}"")
        
        # Initialize Azure Table Storage client
        if not HAS_AZURE_TABLES:
             print(""[ERROR!] azure-data-tables package is not installed. Cannot log to Azure Table Storage."")
        elif not table_connection_string:
             print(""[ERROR!] Table connection string is missing. Cannot log to Azure Table Storage."")
        else:
            try:
                table_service = TableServiceClient.from_connection_string(table_connection_string)
                self.table_client = table_service.create_table_if_not_exists(""JobLogs"")
            except Exception as e:
                print(f""[ERROR!] Could not connect to Azure Table Storage: {e}"")
    
    def _get_job_id_from_instance(self) -> int:
        """"""Get the job_id from the job_instance_id.""""""
        if not self.connection or self.job_instance_id <= 0:
            return 0
        try:
            cursor = self.connection.cursor()
            cursor.execute(""""""
                SELECT js.JobId 
                FROM JobInstance ji 
                JOIN JobSchedule js ON ji.JobScheduleId = js.Id 
                WHERE ji.Id = ?
            """""", self.job_instance_id)
            row = cursor.fetchone()
            return row[0] if row else 0
        except Exception as e:
            print(f""Warning: Could not get job_id from instance: {e}"")
            return 0
    
    def _get_partition_key(self) -> str:
        """"""Get the partition key in format '{JobId}-{JobInstanceId}'.""""""
        return f""{self.job_id}-{self.job_instance_id}""
    
    def log_progress(self, message: str, level: str = ""Info""):
        """"""Log progress message to console, database, and Azure Table Storage.""""""
        timestamp = datetime.utcnow().strftime(""%Y-%m-%d %H:%M:%S"")
        print(f""[{timestamp}] [{level}] {message}"")
               
        # Log to Azure Table Storage with partition key ""{JobId}-{JobInstanceId}""
        if self.table_client and self.job_id > 0:
            try:
                entity = {
                    ""PartitionKey"": self._get_partition_key(),
                    ""RowKey"": str(uuid.uuid4()),
                    ""Action"": ""JobProgress"",
                    ""Details"": message,
                    ""Level"": level,
                    ""Timestamp"": datetime.utcnow(),
                    ""JobId"": self.job_id,
                    ""JobInstanceId"": self.job_instance_id
                }
                self.table_client.create_entity(entity)
            except Exception as e:
                print(f""[ERROR!] Failed to log to Azure Table Storage: {e}"")
    
    def log_error(self, message: str, stack_trace: str = """"):
        """"""Log error to console, database, and Azure Table Storage, update job instance status.""""""
        timestamp = datetime.utcnow().strftime(""%Y-%m-%d %H:%M:%S"")
        print(f""[{timestamp}] [ERROR] {message}"")
        
        # Log to SQL Server database
        if self.connection and self.job_instance_id > 0:
            try:
                cursor = self.connection.cursor()
                
                # Update job instance to mark as error
                cursor.execute(""""""
                    UPDATE JobInstances 
                    SET HasError = 1, UpdatedDate = GETUTCDATE(), UpdatedBy = 'JobExecutor'
                    WHERE Id = ?
                """""", self.job_instance_id)
                
                if self.job_id > 0:
                    field_desc = f""Error_{datetime.utcnow().strftime('%Y%m%d%H%M%S')}_{uuid.uuid4().hex[:8]}""
                    error_message = f""{message}\n{stack_trace}"" if stack_trace else message
                    cursor.execute(""""""
                        INSERT INTO JobData (JobId, JobFieldDescription, JobStringValue, CreatedDate, CreatedBy)
                        VALUES (?, ?, ?, GETUTCDATE(), 'JobExecutor')
                    """""", self.job_id, field_desc, error_message)
                
                self.connection.commit()
            except Exception as e:
                print(f""Warning: Failed to log error to database: {e}"")
        
        # Log to Azure Table Storage with partition key ""{JobId}-{JobInstanceId}""
        if self.table_client and self.job_id > 0:
            try:
                error_message = f""{message}\n{stack_trace}"" if stack_trace else message
                entity = {
                    ""PartitionKey"": self._get_partition_key(),
                    ""RowKey"": str(uuid.uuid4()),
                    ""Action"": ""JobError"",
                    ""Details"": error_message,
                    ""Level"": ""Error"",
                    ""Timestamp"": datetime.utcnow(),
                    ""JobId"": self.job_id,
                    ""JobInstanceId"": self.job_instance_id
                }
                self.table_client.create_entity(entity)
            except Exception as e:
                print(f""[ERROR!] Failed to log error to Azure Table Storage: {e}"")
    
    def close(self):
        """"""Close database connection.""""""
        if self.connection:
            self.connection.close()


def execute_job(app_settings: str, job_agent_id: int, job_id: int, job_instance_id: int, job_schedule_id: int, web_api_parameter: str = """") -> list[str]:
    """"""
    Execute the job with the given parameters.
    Logs are partitioned by '{JobId}-{JobInstanceId}' for efficient querying.
    
    Args:
        app_settings: JSON string containing application settings including connection strings
        job_agent_id: The ID of the job agent executing this job
        job_id: The ID of the job
        job_instance_id: The ID of this specific job instance
        job_schedule_id: The ID of the job schedule
        web_api_parameter: Optional parameter for the weather API location
    """"""
    logs = []
    # Parse connection strings from app_settings
    connection_string = """"
    table_connection_string = """"
    try:
        settings = json.loads(app_settings) if app_settings else {}
        connection_strings = settings.get(""ConnectionStrings"", {})
        connection_string = connection_strings.get(""blazororchestratordb"", """")
        table_connection_string = connection_strings.get(""tables"", """")
    except json.JSONDecodeError:
        pass
    
    logger = JobLogger(connection_string, job_instance_id, table_connection_string)
    
    # Get job_id from job_instance_id if not provided
    if job_id <= 0 and logger.job_id > 0:
        job_id = logger.job_id
    
    try:
        logger.log_progress(""Job started"")
        logs.append(""Job started"")
        
        exec_info = f""Executing Job ID: {job_id}, Instance: {job_instance_id}, Schedule: {job_schedule_id}, Agent: {job_agent_id}""
        print(exec_info)
        logger.log_progress(exec_info)
        logs.append(exec_info)
        
        partition_key_info = f""Log partition key: {job_id}-{job_instance_id}""
        print(partition_key_info)
        logger.log_progress(partition_key_info)
        logs.append(partition_key_info)
        
        # Check for previous run time
        if logger.connection and job_id > 0:
            try:
                cursor = logger.connection.cursor()
                cursor.execute(""""""
                    SELECT JobDateValue 
                    FROM JobData 
                    WHERE JobId = ? AND JobFieldDescription = 'Last Job Run Time'
                """""", job_id)
                row = cursor.fetchone()
                if row and row[0]:
                    prev_run_time = row[0]
                    # Convert to local time (assuming prev_run_time is UTC)
                    local_time = prev_run_time + (datetime.now() - datetime.utcnow())
                    # Format: 00/00/0000 00:00(am/pm) -> %m/%d/%Y %I:%M%p
                    formatted_time = local_time.strftime(""%m/%d/%Y %I:%M"") + local_time.strftime(""%p"").lower()
                    prev_run_msg = f""Previous time the job was run: {formatted_time}""
                    print(prev_run_msg)
                    logger.log_progress(prev_run_msg)
                    logs.append(prev_run_msg)
            except Exception as e:
                print(f""Warning: Failed to read Last Job Run Time: {e}"")
        
        # Set default weather API parameter
        weather_api_param = ""Los+Angeles,CA""
        weather_location = ""Los Angeles,CA""
        
        # If web_api_parameter is passed, use it to fetch weather data
        if web_api_parameter:
            weather_api_param = web_api_parameter.replace("" "", ""+"")
            weather_location = web_api_parameter
        
        # Fetch weather data for the specified location
        logger.log_progress(f""Fetching weather data for {weather_location}"")
        logs.append(f""Fetching weather data for {weather_location}"")
        
        # Using wttr.in as a free weather API (weather.com requires API key)
        weather_url = f""https://wttr.in/{weather_api_param}?format=j1""
        
        try:
            req = urllib.request.Request(
                weather_url,
                headers={""User-Agent"": ""BlazorDataOrchestrator/1.0""}
            )
            with urllib.request.urlopen(req, timeout=30) as response:
                weather_data = json.loads(response.read().decode(""utf-8""))
                
                # Extract current weather information
                current_condition = weather_data[""current_condition""][0]
                temp_c = current_condition[""temp_C""]
                temp_f = current_condition[""temp_F""]
                humidity = current_condition[""humidity""]
                weather_desc = current_condition[""weatherDesc""][0][""value""]
                
                weather_info = f""{weather_location} - Temperature: {temp_f}°F ({temp_c}°C), Humidity: {humidity}%, Conditions: {weather_desc}""
                print(weather_info)
                logger.log_progress(weather_info)
                logs.append(weather_info)
                
        except urllib.error.URLError as e:
            error_msg = f""Failed to fetch weather data: {e.reason}""
            print(error_msg)
            logger.log_progress(error_msg, ""Warning"")
            logs.append(error_msg)
        except Exception as e:
            error_msg = f""Error processing weather data: {str(e)}""
            print(error_msg)
            logger.log_progress(error_msg, ""Warning"")
            logs.append(error_msg)
        
        logger.log_progress(""Job completed successfully!"")
        logs.append(""Job completed successfully!"")
        
    except Exception as e:
        import traceback
        error_msg = f""Job execution error: {str(e)}""
        stack_trace = traceback.format_exc()
        logger.log_error(error_msg, stack_trace)
        logs.append(error_msg)
        raise
    finally:
        # Update Last Job Run Time
        if logger.connection and job_id > 0:
            try:
                cursor = logger.connection.cursor()
                cursor.execute(""""""
                    SELECT Id FROM JobData 
                    WHERE JobId = ? AND JobFieldDescription = 'Last Job Run Time'
                """""", job_id)
                row = cursor.fetchone()
                
                current_time = datetime.utcnow()
                
                if row:
                    cursor.execute(""""""
                        UPDATE JobData 
                        SET JobDateValue = ?, UpdatedDate = GETUTCDATE(), UpdatedBy = 'JobExecutor'
                        WHERE Id = ?
                    """""", current_time, row[0])
                else:
                    cursor.execute(""""""
                        INSERT INTO JobData (JobId, JobFieldDescription, JobDateValue, CreatedDate, CreatedBy)
                        VALUES (?, 'Last Job Run Time', ?, GETUTCDATE(), 'JobExecutor')
                    """""", job_id, current_time)
                logger.connection.commit()
            except Exception as e:
                print(f""Warning: Failed to update Last Job Run Time: {e}"")

        logger.close()
    
    return logs
";

    private const string DefaultAppSettings = @"{
  ""ConnectionStrings"": {
    ""blobs"": ""UseDevelopmentStorage=true"",
    ""tables"": ""UseDevelopmentStorage=true"",
    ""queues"": ""UseDevelopmentStorage=true"",
    ""blazororchestratordb"": ""Server=127.0.0.1,1433;Database=blazororchestratordb;User ID=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=true""
  },
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }
  },
  ""AllowedHosts"": ""*""
}";

    public JobCodeEditorService(JobManager jobManager, IConfiguration configuration, ILogger<JobCodeEditorService> logger)
    {
        _jobManager = jobManager;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the default template for the specified language.
    /// </summary>
    /// <param name="language">The programming language (csharp or python).</param>
    /// <returns>The default code template.</returns>
    public string GetDefaultTemplate(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "cs" => DefaultCSharpTemplate,
            "python" or "py" => DefaultPythonTemplate,
            _ => DefaultCSharpTemplate
        };
    }

    /// <summary>
    /// Gets the default appsettings content.
    /// </summary>
    /// <returns>Default appsettings JSON.</returns>
    public string GetDefaultAppSettings()
    {
        return DefaultAppSettings;
    }

    /// <summary>
    /// Loads job code from an existing NuGet package in blob storage.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>The job code model, or null if not found.</returns>
    public async Task<JobCodeModel?> LoadJobCodeAsync(int jobId)
    {
        try
        {
            // Get the job's code file from the database
            var jobCodeFile = await _jobManager.GetJobCodeFileAsync(jobId);
            
            if (string.IsNullOrEmpty(jobCodeFile))
            {
                _logger.LogInformation("No code file found for job {JobId}, returning default template", jobId);
                return null;
            }

            // Download the package from blob storage
            var packageStream = await _jobManager.DownloadJobPackageAsync(jobId);
            
            if (packageStream == null)
            {
                _logger.LogWarning("Failed to download package for job {JobId}", jobId);
                return null;
            }

            // Extract code from the NuGet package
            return await ExtractCodeFromPackageAsync(packageStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading job code for job {JobId}", jobId);
            return null;
        }
    }

    /// <summary>
    /// Extracts code files from a NuGet package stream.
    /// </summary>
    /// <param name="packageStream">The NuGet package stream.</param>
    /// <returns>The extracted job code model.</returns>
    public async Task<JobCodeModel> ExtractCodeFromPackageAsync(Stream packageStream)
    {
        var model = new JobCodeModel();
        
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        
        foreach (var entry in archive.Entries)
        {
            var entryPath = entry.FullName.Replace('\\', '/').ToLowerInvariant();
            
            // Look for code files in contentFiles/any/any/ structure
            if (entryPath.Contains("codecsharp/main.cs") || entryPath.EndsWith("/main.cs"))
            {
                using var reader = new StreamReader(entry.Open());
                model.MainCode = await reader.ReadToEndAsync();
                model.Language = "csharp";
            }
            else if (entryPath.Contains("codepython/main.py") || entryPath.EndsWith("/main.py"))
            {
                using var reader = new StreamReader(entry.Open());
                model.MainCode = await reader.ReadToEndAsync();
                model.Language = "python";
            }
            else if (entryPath.EndsWith("appsettings.json") && !entryPath.Contains("production"))
            {
                using var reader = new StreamReader(entry.Open());
                model.AppSettings = await reader.ReadToEndAsync();
            }
            else if (entryPath.EndsWith("appsettingsproduction.json") || entryPath.Contains("appsettings.production.json"))
            {
                using var reader = new StreamReader(entry.Open());
                model.AppSettingsProduction = await reader.ReadToEndAsync();
            }
            else if (entryPath.EndsWith("configuration.json"))
            {
                using var reader = new StreamReader(entry.Open());
                var configJson = await reader.ReadToEndAsync();
                try
                {
                    var config = JsonSerializer.Deserialize<ConfigurationModel>(configJson);
                    if (config != null && !string.IsNullOrEmpty(config.SelectedLanguage))
                    {
                        model.Language = config.SelectedLanguage;
                    }
                }
                catch
                {
                    // Ignore configuration parsing errors
                }
            }
            else if (entryPath.Contains("requirements.txt"))
            {
                using var reader = new StreamReader(entry.Open());
                model.RequirementsTxt = await reader.ReadToEndAsync();
            }
        }

        // Set defaults if not found
        if (string.IsNullOrEmpty(model.MainCode))
        {
            model.MainCode = GetDefaultTemplate(model.Language);
        }
        if (string.IsNullOrEmpty(model.AppSettings))
        {
            model.AppSettings = DefaultAppSettings;
        }
        if (string.IsNullOrEmpty(model.AppSettingsProduction))
        {
            model.AppSettingsProduction = DefaultAppSettings;
        }

        return model;
    }

    /// <summary>
    /// Extracts all relevant files from a NuGet package based on the selected language.
    /// </summary>
    /// <param name="packageStream">The NuGet package stream.</param>
    /// <param name="language">The programming language (csharp or python).</param>
    /// <returns>A JobCodeModel with all extracted files.</returns>
    public async Task<JobCodeModel> ExtractAllFilesFromPackageAsync(Stream packageStream, string language)
    {
        var model = new JobCodeModel { Language = language };
        var codeExtension = language.ToLower() == "python" ? ".py" : ".cs";
        var mainFileName = language.ToLower() == "python" ? "main.py" : "main.cs";

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var entryPath = entry.FullName.Replace('\\', '/');
            var fileName = Path.GetFileName(entryPath);
            var lowerFileName = fileName.ToLowerInvariant();
            var lowerPath = entryPath.ToLowerInvariant();

            // Skip directories and metadata files (but keep .nuspec for C#)
            if (string.IsNullOrEmpty(fileName) ||
                lowerPath.Contains("[content_types]") ||
                lowerPath.Contains("_rels/"))
            {
                continue;
            }

            // Handle .nuspec file specially - extract for C# language
            if (lowerPath.EndsWith(".nuspec"))
            {
                // Only include .nuspec for C# language
                if (language.ToLower() == "csharp" || language.ToLower() == "cs")
                {
                    using var nuspecReader = new StreamReader(entry.Open());
                    model.NuspecContent = await nuspecReader.ReadToEndAsync();
                    model.NuspecFileName = fileName;
                    
                    // Parse dependencies from .nuspec
                    model.Dependencies = ParseNuSpecDependencies(model.NuspecContent);
                    
                    // Add to discovered files so it shows in dropdown
                    if (!model.DiscoveredFiles.Contains(fileName))
                    {
                        model.DiscoveredFiles.Add(fileName);
                    }
                }
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            var content = await reader.ReadToEndAsync();

            // Check for main code file
            if (lowerFileName == mainFileName)
            {
                model.MainCode = content;
                if (!model.DiscoveredFiles.Contains(mainFileName))
                {
                    model.DiscoveredFiles.Insert(0, mainFileName); // Main file first
                }
            }
            // Check for appsettings files
            else if (lowerFileName == "appsettings.json")
            {
                model.AppSettings = content;
                if (!model.DiscoveredFiles.Contains("appsettings.json"))
                {
                    model.DiscoveredFiles.Add("appsettings.json");
                }
            }
            else if (lowerFileName == "appsettings.production.json" ||
                     lowerFileName == "appsettingsproduction.json")
            {
                model.AppSettingsProduction = content;
                if (!model.DiscoveredFiles.Contains("appsettings.Production.json"))
                {
                    model.DiscoveredFiles.Add("appsettings.Production.json");
                }
            }
            // Check for additional code files
            else if (fileName.EndsWith(codeExtension, StringComparison.OrdinalIgnoreCase))
            {
                model.AdditionalCodeFiles[fileName] = content;
                if (!model.DiscoveredFiles.Contains(fileName))
                {
                    model.DiscoveredFiles.Add(fileName);
                }
            }
            // Check for requirements.txt (Python)
            else if (lowerFileName == "requirements.txt" && language == "python")
            {
                model.RequirementsTxt = content;
                if (!model.DiscoveredFiles.Contains("requirements.txt"))
                {
                    model.DiscoveredFiles.Add("requirements.txt");
                }
            }
            // Check for configuration.json
            else if (lowerFileName == "configuration.json")
            {
                try
                {
                    var config = JsonSerializer.Deserialize<ConfigurationModel>(content);
                    if (config != null && !string.IsNullOrEmpty(config.SelectedLanguage))
                    {
                        model.Language = config.SelectedLanguage;
                    }
                }
                catch
                {
                    // Ignore configuration parsing errors
                }
            }
        }

        // Ensure default file order: main file, appsettings, appsettings.Production, then others
        var orderedFiles = new List<string>();

        // Add main file first
        if (model.DiscoveredFiles.Contains(mainFileName))
        {
            orderedFiles.Add(mainFileName);
        }

        // Add config files
        if (model.DiscoveredFiles.Contains("appsettings.json"))
        {
            orderedFiles.Add("appsettings.json");
        }
        if (model.DiscoveredFiles.Contains("appsettings.Production.json"))
        {
            orderedFiles.Add("appsettings.Production.json");
        }

        // Add requirements.txt for Python
        if (language == "python" && model.DiscoveredFiles.Contains("requirements.txt"))
        {
            orderedFiles.Add("requirements.txt");
        }

        // Add remaining code files
        foreach (var file in model.DiscoveredFiles)
        {
            if (!orderedFiles.Contains(file))
            {
                orderedFiles.Add(file);
            }
        }

        model.DiscoveredFiles = orderedFiles;

        // Set defaults if main code not found
        if (string.IsNullOrEmpty(model.MainCode))
        {
            model.MainCode = GetDefaultTemplate(language);
            if (!model.DiscoveredFiles.Contains(mainFileName))
            {
                model.DiscoveredFiles.Insert(0, mainFileName);
            }
        }
        if (string.IsNullOrEmpty(model.AppSettings))
        {
            model.AppSettings = DefaultAppSettings;
            if (!model.DiscoveredFiles.Contains("appsettings.json"))
            {
                model.DiscoveredFiles.Add("appsettings.json");
            }
        }
        if (string.IsNullOrEmpty(model.AppSettingsProduction))
        {
            model.AppSettingsProduction = DefaultAppSettings;
            if (!model.DiscoveredFiles.Contains("appsettings.Production.json"))
            {
                model.DiscoveredFiles.Add("appsettings.Production.json");
            }
        }

        return model;
    }

    /// <summary>
    /// Parses NuGet dependencies from .nuspec XML content.
    /// </summary>
    /// <param name="nuspecContent">The .nuspec file content.</param>
    /// <returns>List of parsed NuGet dependencies.</returns>
    private List<NuGetDependencyInfo> ParseNuSpecDependencies(string nuspecContent)
    {
        var dependencies = new List<NuGetDependencyInfo>();
        
        try
        {
            var doc = XDocument.Parse(nuspecContent);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            
            // Find all dependency elements
            var dependencyElements = doc.Descendants(ns + "dependency");
            foreach (var dep in dependencyElements)
            {
                var packageId = dep.Attribute("id")?.Value;
                var version = dep.Attribute("version")?.Value;
                
                if (!string.IsNullOrEmpty(packageId))
                {
                    // Get target framework from parent group if available
                    var targetFramework = dep.Parent?.Attribute("targetFramework")?.Value ?? "";
                    
                    dependencies.Add(new NuGetDependencyInfo
                    {
                        PackageId = packageId,
                        Version = version ?? "",
                        TargetFramework = targetFramework
                    });
                }
            }
            
            _logger.LogInformation("Parsed {Count} dependencies from .nuspec", dependencies.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse .nuspec dependencies");
        }
        
        return dependencies;
    }

    /// <summary>
    /// Gets the content of a specific file from the JobCodeModel.
    /// </summary>
    /// <param name="model">The job code model.</param>
    /// <param name="fileName">The name of the file to retrieve.</param>
    /// <returns>The file content, or empty string if not found.</returns>
    public string GetFileContent(JobCodeModel model, string fileName)
    {
        var lowerFileName = fileName.ToLowerInvariant();
        var mainFileName = model.Language.ToLower() == "python" ? "main.py" : "main.cs";

        // Check main file
        if (lowerFileName == mainFileName.ToLower())
        {
            return model.MainCode;
        }

        // Check config files
        if (lowerFileName == "appsettings.json")
        {
            return model.AppSettings;
        }
        if (lowerFileName == "appsettings.production.json")
        {
            return model.AppSettingsProduction;
        }

        // Check requirements.txt
        if (lowerFileName == "requirements.txt" && !string.IsNullOrEmpty(model.RequirementsTxt))
        {
            return model.RequirementsTxt;
        }

        // Check .nuspec file
        if (lowerFileName.EndsWith(".nuspec") && !string.IsNullOrEmpty(model.NuspecContent))
        {
            return model.NuspecContent;
        }

        // Check additional files
        if (model.AdditionalCodeFiles.TryGetValue(fileName, out var content))
        {
            return content;
        }

        // Case-insensitive search in additional files
        var matchingKey = model.AdditionalCodeFiles.Keys
            .FirstOrDefault(k => k.Equals(fileName, StringComparison.OrdinalIgnoreCase));

        if (matchingKey != null)
        {
            return model.AdditionalCodeFiles[matchingKey];
        }

        return "";
    }

    /// <summary>
    /// Updates the content of a specific file in the JobCodeModel.
    /// </summary>
    /// <param name="model">The job code model.</param>
    /// <param name="fileName">The name of the file to update.</param>
    /// <param name="content">The new content.</param>
    public void SetFileContent(JobCodeModel model, string fileName, string content)
    {
        var lowerFileName = fileName.ToLowerInvariant();
        var mainFileName = model.Language.ToLower() == "python" ? "main.py" : "main.cs";

        if (lowerFileName == mainFileName.ToLower())
        {
            model.MainCode = content;
        }
        else if (lowerFileName == "appsettings.json")
        {
            model.AppSettings = content;
        }
        else if (lowerFileName == "appsettings.production.json")
        {
            model.AppSettingsProduction = content;
        }
        else if (lowerFileName == "requirements.txt")
        {
            model.RequirementsTxt = content;
        }
        else if (lowerFileName.EndsWith(".nuspec"))
        {
            model.NuspecContent = content;
            model.NuspecFileName = fileName;
        }
        else
        {
            model.AdditionalCodeFiles[fileName] = content;
        }
    }

    /// <summary>
    /// Gets the file list based on the selected language.
    /// </summary>
    /// <param name="language">The programming language.</param>
    /// <returns>List of file names.</returns>
    public List<string> GetFileListForLanguage(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "cs" => new List<string> { "main.cs", "appsettings.json", "appsettings.Production.json" },
            "python" or "py" => new List<string> { "main.py", "requirements.txt", "appsettings.json", "appsettings.Production.json" },
            _ => new List<string> { "main.cs", "appsettings.json", "appsettings.Production.json" }
        };
    }

    /// <summary>
    /// Gets the Monaco editor language string for a file.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="defaultLanguage">The default language if not determinable from file extension.</param>
    /// <returns>The Monaco language identifier.</returns>
    public string GetMonacoLanguageForFile(string? fileName, string defaultLanguage = "csharp")
    {
        if (string.IsNullOrEmpty(fileName))
            return defaultLanguage;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".py" => "python",
            ".json" => "json",
            ".txt" => "plaintext",
            ".xml" => "xml",
            ".nuspec" => "xml",  // .nuspec files are XML format
            _ => defaultLanguage
        };
    }

    private class ConfigurationModel
    {
        public string? SelectedLanguage { get; set; }
        public int LastJobId { get; set; }
        public int LastJobInstanceId { get; set; }
    }
}

/// <summary>
/// Model representing job code and configuration.
/// </summary>
public class JobCodeModel
{
    /// <summary>
    /// The main code content (Program.cs or main.py).
    /// </summary>
    public string MainCode { get; set; } = "";

    /// <summary>
    /// The programming language (csharp or python).
    /// </summary>
    public string Language { get; set; } = "csharp";

    /// <summary>
    /// The appsettings.json content.
    /// </summary>
    public string AppSettings { get; set; } = "{}";

    /// <summary>
    /// The appsettings.Production.json content.
    /// </summary>
    public string AppSettingsProduction { get; set; } = "{}";

    /// <summary>
    /// The .nuspec file content (for C# packages).
    /// Contains package metadata and NuGet dependencies.
    /// </summary>
    public string? NuspecContent { get; set; }

    /// <summary>
    /// The .nuspec file name as found in the package.
    /// </summary>
    public string? NuspecFileName { get; set; }

    /// <summary>
    /// The requirements.txt content (for Python).
    /// </summary>
    public string? RequirementsTxt { get; set; }

    /// <summary>
    /// Dictionary to store additional code files.
    /// Key = filename, Value = file content.
    /// </summary>
    public Dictionary<string, string> AdditionalCodeFiles { get; set; } = new();

    /// <summary>
    /// List of all discovered files for the dropdown, in display order.
    /// </summary>
    public List<string> DiscoveredFiles { get; set; } = new();

    /// <summary>
    /// List of NuGet dependencies parsed from the .nuspec file.
    /// </summary>
    public List<NuGetDependencyInfo> Dependencies { get; set; } = new();
}

/// <summary>
/// Information about an extracted file from a NuGet package.
/// </summary>
public class ExtractedFileInfo
{
    /// <summary>
    /// The file name.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// The relative path within the package.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// The file content.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Indicates if this is the main code file (main.cs, main.py, Program.cs).
    /// </summary>
    public bool IsMainFile { get; set; }

    /// <summary>
    /// Indicates if this is a configuration file (appsettings.json, etc.).
    /// </summary>
    public bool IsConfigFile { get; set; }
}

/// <summary>
/// Result of code compilation.
/// </summary>
public class CompilationResult
{
    /// <summary>
    /// Indicates whether compilation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// List of compilation errors.
    /// </summary>
    public List<CompilationError> Errors { get; set; } = new();
}

/// <summary>
/// Represents a compilation error or warning.
/// </summary>
public class CompilationError
{
    /// <summary>
    /// The error message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// The line number where the error occurred.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// The column number where the error occurred.
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// The severity level (Error or Warning).
    /// </summary>
    public string Severity { get; set; } = "Error";
}

/// <summary>
/// Represents a NuGet package dependency from a .nuspec file.
/// </summary>
public class NuGetDependencyInfo
{
    /// <summary>
    /// The NuGet package ID (e.g., "Newtonsoft.Json").
    /// </summary>
    public string PackageId { get; set; } = "";

    /// <summary>
    /// The version or version range (e.g., "13.0.1", "[13.0.1,)").
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// The target framework (e.g., "net8.0", "net9.0").
    /// </summary>
    public string TargetFramework { get; set; } = "";
}
