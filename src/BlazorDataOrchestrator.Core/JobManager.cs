using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using BlazorOrchestrator.Web.Data.Data;
using CSScriptLib;
using Microsoft.EntityFrameworkCore;

namespace BlazorDataOrchestrator.Core
{
    /// <summary>
    /// Represents a log entry from Azure Table Storage.
    /// </summary>
    public class JobLogEntry
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
        public string Level { get; set; } = "Info";
        public DateTime Timestamp { get; set; }
        public int JobId { get; set; }
        public int JobInstanceId { get; set; }
    }

    public class JobManager
    {
        private readonly string _sqlConnectionString;
        private readonly string _blobConnectionString;
        private readonly string _queueConnectionString;
        private readonly string _tableConnectionString;
        private readonly TableClient _logTableClient;
        private readonly QueueClient _jobQueueClient;
        private readonly BlobContainerClient _packageContainerClient;

        public JobManager(string sqlConnectionString, string blobConnectionString, string queueConnectionString, string tableConnectionString)
        {
            _sqlConnectionString = sqlConnectionString;
            _blobConnectionString = blobConnectionString;
            _queueConnectionString = queueConnectionString;
            _tableConnectionString = tableConnectionString;

            // Initialize clients
            var tableServiceClient = new TableServiceClient(_tableConnectionString);
            _logTableClient = tableServiceClient.GetTableClient("JobLogs");
            _logTableClient.CreateIfNotExists();

            var queueServiceClient = new QueueServiceClient(_queueConnectionString);
            _jobQueueClient = queueServiceClient.GetQueueClient("job-queue");
            _jobQueueClient.CreateIfNotExists();

            var blobServiceClient = new BlobServiceClient(_blobConnectionString);
            _packageContainerClient = blobServiceClient.GetBlobContainerClient("job-packages");
            _packageContainerClient.CreateIfNotExists();
        }

        private ApplicationDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(_sqlConnectionString);
            return new ApplicationDbContext(optionsBuilder.Options);
        }

        private async Task LogAsync(string action, string details, string level = "Info", int? jobId = null, int? jobInstanceId = null)
        {
            try
            {
                // Partition key format: {JobId}-{JobInstanceId} if available, otherwise fallback to date-based partitioning
                string partitionKey = jobId.HasValue && jobInstanceId.HasValue
                    ? $"{jobId.Value}-{jobInstanceId.Value}"
                    : jobId.HasValue
                        ? $"{jobId.Value}-0"
                        : DateTime.UtcNow.ToString("yyyyMMdd");

                var entity = new TableEntity(partitionKey, Guid.NewGuid().ToString())
                {
                    { "Action", action },
                    { "Details", details },
                    { "Level", level },
                    { "Timestamp", DateTime.UtcNow },
                    { "JobId", jobId ?? 0 },
                    { "JobInstanceId", jobInstanceId ?? 0 }
                };
                await _logTableClient.AddEntityAsync(entity);
            }
            catch
            {
                // Fail silently if logging fails to avoid breaking the flow, or handle appropriately
            }
        }

        /// <summary>
        /// Gets the latest job instance ID for a given job ID.
        /// </summary>
        /// <param name="jobId">The job ID to look up</param>
        /// <returns>The latest job instance ID, or null if not found</returns>
        public async Task<int?> GetLatestJobInstanceIdAsync(int jobId)
        {
            using var context = CreateDbContext();
            var latestInstance = await context.JobInstances
                .Include(i => i.JobSchedule)
                .Where(i => i.JobSchedule.JobId == jobId)
                .OrderByDescending(i => i.CreatedDate)
                .FirstOrDefaultAsync();

            return latestInstance?.Id;
        }

        /// <summary>
        /// Gets log entries for a specific job and job instance from Azure Table Storage.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <param name="jobInstanceId">The job instance ID</param>
        /// <param name="maxResults">Maximum number of results to return (default 100)</param>
        /// <returns>List of log entries</returns>
        public async Task<List<JobLogEntry>> GetLogsForJobInstanceAsync(int jobId, int jobInstanceId, int maxResults = 100)
        {
            var logs = new List<JobLogEntry>();
            try
            {
                string partitionKey = $"{jobId}-{jobInstanceId}";
                var queryResults = _logTableClient.QueryAsync<TableEntity>(
                    filter: $"PartitionKey eq '{partitionKey}'",
                    maxPerPage: maxResults);

                await foreach (var entity in queryResults)
                {
                    logs.Add(new JobLogEntry
                    {
                        PartitionKey = entity.PartitionKey,
                        RowKey = entity.RowKey,
                        Action = entity.GetString("Action"),
                        Details = entity.GetString("Details"),
                        Level = entity.GetString("Level"),
                        Timestamp = entity.GetDateTime("Timestamp") ?? DateTime.UtcNow,
                        JobId = entity.GetInt32("JobId") ?? 0,
                        JobInstanceId = entity.GetInt32("JobInstanceId") ?? 0
                    });
                }
            }
            catch
            {
                // Return empty list on error
            }
            return logs.OrderByDescending(l => l.Timestamp).Take(maxResults).ToList();
        }

        /// <summary>
        /// Gets logs for the latest job instance of a given job.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <param name="maxResults">Maximum number of results to return</param>
        /// <returns>List of log entries for the latest job instance</returns>
        public async Task<List<JobLogEntry>> GetLogsForLatestJobInstanceAsync(int jobId, int maxResults = 100)
        {
            var latestInstanceId = await GetLatestJobInstanceIdAsync(jobId);
            if (!latestInstanceId.HasValue)
            {
                return new List<JobLogEntry>();
            }
            return await GetLogsForJobInstanceAsync(jobId, latestInstanceId.Value, maxResults);
        }

        // #1 Create New Job
        public async Task<int> CreateNewJobAsync(Job job, string? jobGroupName = null, string? jobOrganizationName = null)
        {
            using var context = CreateDbContext();
            await LogAsync("CreateNewJob", $"Creating job {job.JobName}");

            // Organization
            jobOrganizationName ??= "Default";
            var org = await context.JobOrganizations.FirstOrDefaultAsync(o => o.OrganizationName == jobOrganizationName);
            if (org == null)
            {
                org = new JobOrganization
                {
                    OrganizationName = jobOrganizationName,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = "System"
                };
                context.JobOrganizations.Add(org);
                await context.SaveChangesAsync();
                await LogAsync("CreateNewJob", $"Created Organization {jobOrganizationName}");
            }
            job.JobOrganizationId = org.Id;

            // Job
            job.CreatedDate = DateTime.UtcNow;
            context.Jobs.Add(job);
            await context.SaveChangesAsync();

            // Group
            jobGroupName ??= "Default";
            var group = await context.JobGroups.FirstOrDefaultAsync(g => g.JobGroupName == jobGroupName);
            if (group == null)
            {
                group = new JobGroup
                {
                    JobGroupName = jobGroupName,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = "System"
                };
                context.JobGroups.Add(group);
                await context.SaveChangesAsync();
                await LogAsync("CreateNewJob", $"Created Group {jobGroupName}");
            }

            var jobJobGroup = new JobJobGroup
            {
                JobId = job.Id,
                JobGroupId = group.Id
            };
            context.JobJobGroups.Add(jobJobGroup);
            await context.SaveChangesAsync();

            await LogAsync("CreateNewJob", $"Job {job.JobName} created with ID {job.Id}");
            return job.Id;
        }

        // #2 Delete Job
        public async Task DeleteJobAsync(int jobId)
        {
            using var context = CreateDbContext();
            await LogAsync("DeleteJob", $"Deleting job {jobId}");

            var job = await context.Jobs
                .Include(j => j.JobSchedules)
                .Include(j => j.JobJobGroups)
                .Include(j => j.JobData)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job != null)
            {
                context.JobSchedules.RemoveRange(job.JobSchedules);
                context.JobJobGroups.RemoveRange(job.JobJobGroups);
                context.JobData.RemoveRange(job.JobData);
                context.Jobs.Remove(job);
                
                await context.SaveChangesAsync();
                await LogAsync("DeleteJob", $"Job {jobId} deleted");
            }
            else
            {
                await LogAsync("DeleteJob", $"Job {jobId} not found", "Warning");
            }
        }

        // #3 Create New Job Schedule
        public async Task<int> CreateNewJobScheduleAsync(JobSchedule schedule)
        {
            using var context = CreateDbContext();
            await LogAsync("CreateNewJobSchedule", $"Creating schedule for Job {schedule.JobId}");

            schedule.CreatedDate = DateTime.UtcNow;
            context.JobSchedules.Add(schedule);
            await context.SaveChangesAsync();

            await LogAsync("CreateNewJobSchedule", $"Schedule {schedule.Id} created");
            return schedule.Id;
        }

        // #4 Create New Job Instance
        public async Task<int> CreateNewJobInstanceAsync(int jobScheduleId)
        {
            using var context = CreateDbContext();
            await LogAsync("CreateNewJobInstance", $"Creating instance for Schedule {jobScheduleId}");

            var instance = new JobInstance
            {
                JobScheduleId = jobScheduleId,
                InProcess = false,
                HasError = false,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = "System"
            };
            context.JobInstances.Add(instance);
            await context.SaveChangesAsync();

            // Add to Queue
            var message = System.Text.Json.JsonSerializer.Serialize(new { JobInstanceId = instance.Id });
            await _jobQueueClient.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message)));

            await LogAsync("CreateNewJobInstance", $"Instance {instance.Id} created and queued");
            return instance.Id;
        }

        // #5 Run Job
        public async Task RunJobAsync(int jobInstanceId)
        {
            using var context = CreateDbContext();
            await LogAsync("RunJob", $"Starting execution for Instance {jobInstanceId}");

            var instance = await context.JobInstances
                .Include(i => i.JobSchedule)
                .ThenInclude(s => s.Job)
                .ThenInclude(j => j.JobData)
                .FirstOrDefaultAsync(i => i.Id == jobInstanceId);

            if (instance == null)
            {
                await LogAsync("RunJob", $"Instance {jobInstanceId} not found", "Error");
                return;
            }

            instance.InProcess = true;
            await context.SaveChangesAsync();

            string tempDir = Path.Combine(Path.GetTempPath(), "BlazorDataOrchestrator", Guid.NewGuid().ToString());

            try
            {
                var job = instance.JobSchedule.Job;
                
                // Retrieve NuGet package
                // Assuming package name is in JobData or we use a convention.
                var packageNameDatum = job.JobData.FirstOrDefault(d => d.JobFieldDescription == "PackageName");
                string packageName = packageNameDatum?.JobStringValue ?? "DefaultPackage.nupkg";

                var blobClient = _packageContainerClient.GetBlobClient(packageName);
                if (!await blobClient.ExistsAsync())
                {
                     throw new Exception($"Package {packageName} not found in blob storage.");
                }

                Directory.CreateDirectory(tempDir);
                string packagePath = Path.Combine(tempDir, packageName);

                await blobClient.DownloadToAsync(packagePath);
                await LogAsync("RunJob", $"Downloaded package {packageName}");

                // Unzip
                ZipFile.ExtractToDirectory(packagePath, tempDir);
                await LogAsync("RunJob", $"Unzipped package to {tempDir}");

                // Execute
                string scriptPath = Path.Combine(tempDir, job.JobCodeFile);
                if (!File.Exists(scriptPath))
                {
                     var files = Directory.GetFiles(tempDir, job.JobCodeFile, SearchOption.AllDirectories);
                     if (files.Length > 0) scriptPath = files[0];
                     else throw new FileNotFoundException($"Script {job.JobCodeFile} not found in package.");
                }

                string extension = Path.GetExtension(scriptPath).ToLower();
                if (extension == ".cs")
                {
                    // Use CSScript
                    await LogAsync("RunJob", $"Executing C# script {scriptPath}");

                    var evaluator = CSScript.Evaluator;

                    // Add references to all DLLs found in the package
                    var dlls = Directory.GetFiles(tempDir, "*.dll", SearchOption.AllDirectories);
                    foreach (var dll in dlls)
                    {
                        evaluator.ReferenceAssembly(dll);
                        await LogAsync("RunJob", $"Added reference: {Path.GetFileName(dll)}", "Debug");
                    }
                    
                    // Load and execute
                    dynamic script = evaluator.LoadFile(scriptPath);
                    
                    // Let's try to find a static Main method in any type.
                    var asm = (System.Reflection.Assembly)script;
                    var entryPoint = asm.EntryPoint;
                    if (entryPoint != null)
                    {
                        var parameters = entryPoint.GetParameters().Length == 0 ? null : new object[] { new string[0] };
                        entryPoint.Invoke(null, parameters);
                    }
                    else
                    {
                        // Look for a static method "Run" or "Main" in any public type
                        bool executed = false;
                        foreach (var type in asm.GetExportedTypes())
                        {
                            var method = type.GetMethod("Main", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (method == null) method = type.GetMethod("Run", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                            
                            if (method != null)
                            {
                                var parameters = method.GetParameters().Length == 0 ? null : new object[] { new string[0] }; // Simplified
                                method.Invoke(null, parameters);
                                executed = true;
                                break;
                            }
                        }
                        
                        if (!executed)
                        {
                             await LogAsync("RunJob", "No entry point (Main/Run) found in C# script.", "Warning");
                        }
                    }
                }
                else if (extension == ".py")
                {
                    // Use CSSnakes
                    await LogAsync("RunJob", $"Executing Python script {scriptPath}");
                    
                    // Note: CSSnakes requires an initialized IPythonEnvironment.
                    // Since we don't have it injected here, we can't easily use it.
                    // This is a placeholder for where CSSnakes logic would go.
                    // Example:
                    // var env = ...; // Get environment
                    // env.RunScript(scriptPath);
                    
                    await LogAsync("RunJob", "Python execution requires configured CSSnakes environment.", "Warning");
                }

                await LogAsync("RunJob", $"Job {job.JobName} executed successfully");
                
                instance.InProcess = false;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                await LogAsync("RunJob", $"Error executing job: {ex.Message}", "Error");
                instance.InProcess = false;
                instance.HasError = true;
                await context.SaveChangesAsync();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        /// <summary>
        /// Logs progress for a job instance. This method is designed to be called from dynamically executed job scripts.
        /// Logs to both the database (JobData table) and Azure Table Storage with partition key "{JobId}-{JobInstanceId}".
        /// </summary>
        /// <param name="dbContext">The database context</param>
        /// <param name="jobInstanceId">The job instance ID</param>
        /// <param name="message">The log message</param>
        /// <param name="level">The log level (Info, Warning, Error, Debug)</param>
        /// <param name="tableConnectionString">Optional Azure Table Storage connection string for additional logging</param>
        public static async Task LogProgress(ApplicationDbContext? dbContext, int jobInstanceId, string message, string level, string? tableConnectionString = null)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");

            int jobId = 0;
            if (dbContext != null && jobInstanceId > 0)
            {
                try
                {
                    var instance = await dbContext.JobInstances
                        .Include(i => i.JobSchedule)
                        .FirstOrDefaultAsync(i => i.Id == jobInstanceId);

                    jobId = instance?.JobSchedule?.JobId ?? 0;

                    var jobData = new JobDatum
                    {
                        JobId = jobId,
                        JobFieldDescription = $"Log_{level}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
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

            // Also log to Azure Table Storage with partition key "{JobId}-{JobInstanceId}"
            if (!string.IsNullOrEmpty(tableConnectionString) && jobId > 0)
            {
                try
                {
                    var tableServiceClient = new TableServiceClient(tableConnectionString);
                    var logTableClient = tableServiceClient.GetTableClient("JobLogs");
                    await logTableClient.CreateIfNotExistsAsync();

                    string partitionKey = $"{jobId}-{jobInstanceId}";
                    var entity = new TableEntity(partitionKey, Guid.NewGuid().ToString())
                    {
                        { "Action", "JobProgress" },
                        { "Details", message },
                        { "Level", level },
                        { "Timestamp", DateTime.UtcNow },
                        { "JobId", jobId },
                        { "JobInstanceId", jobInstanceId }
                    };
                    await logTableClient.AddEntityAsync(entity);
                }
                catch { /* Fail silently if table logging fails */ }
            }
        }

        /// <summary>
        /// Logs an error for a job instance. This method is designed to be called from dynamically executed job scripts.
        /// Logs to both the database (JobData table) and Azure Table Storage with partition key "{JobId}-{JobInstanceId}".
        /// </summary>
        /// <param name="dbContext">The database context</param>
        /// <param name="jobInstanceId">The job instance ID</param>
        /// <param name="message">The error message</param>
        /// <param name="stackTrace">The stack trace</param>
        /// <param name="tableConnectionString">Optional Azure Table Storage connection string for additional logging</param>
        public static async Task LogError(ApplicationDbContext? dbContext, int jobInstanceId, string message, string? stackTrace, string? tableConnectionString = null)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [ERROR] {message}");

            int jobId = 0;
            if (dbContext != null && jobInstanceId > 0)
            {
                try
                {
                    var instance = await dbContext.JobInstances
                        .Include(i => i.JobSchedule)
                        .FirstOrDefaultAsync(i => i.Id == jobInstanceId);

                    if (instance != null)
                    {
                        jobId = instance.JobSchedule?.JobId ?? 0;
                        instance.HasError = true;
                        instance.UpdatedDate = DateTime.UtcNow;
                        instance.UpdatedBy = "JobExecutor";

                        var jobData = new JobDatum
                        {
                            JobId = jobId,
                            JobFieldDescription = $"Error_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
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

            // Also log to Azure Table Storage with partition key "{JobId}-{JobInstanceId}"
            if (!string.IsNullOrEmpty(tableConnectionString) && jobId > 0)
            {
                try
                {
                    var tableServiceClient = new TableServiceClient(tableConnectionString);
                    var logTableClient = tableServiceClient.GetTableClient("JobLogs");
                    await logTableClient.CreateIfNotExistsAsync();

                    string partitionKey = $"{jobId}-{jobInstanceId}";
                    var entity = new TableEntity(partitionKey, Guid.NewGuid().ToString())
                    {
                        { "Action", "JobError" },
                        { "Details", $"{message}\n{stackTrace}" },
                        { "Level", "Error" },
                        { "Timestamp", DateTime.UtcNow },
                        { "JobId", jobId },
                        { "JobInstanceId", jobInstanceId }
                    };
                    await logTableClient.AddEntityAsync(entity);
                }
                catch { /* Fail silently if table logging fails */ }
            }
        }

        /// <summary>
        /// Gets the job ID from a job instance ID.
        /// </summary>
        /// <param name="dbContext">The database context</param>
        /// <param name="jobInstanceId">The job instance ID</param>
        /// <returns>The job ID, or 0 if not found</returns>
        public static async Task<int> GetJobIdFromInstanceAsync(ApplicationDbContext dbContext, int jobInstanceId)
        {
            if (dbContext == null || jobInstanceId <= 0)
                return 0;

            try
            {
                var instance = await dbContext.JobInstances
                    .Include(i => i.JobSchedule)
                    .FirstOrDefaultAsync(i => i.Id == jobInstanceId);

                return instance?.JobSchedule?.JobId ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
