using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using BlazorDataOrchestrator.Core.Data;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;
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
            _packageContainerClient = blobServiceClient.GetBlobContainerClient("jobs");
            _packageContainerClient.CreateIfNotExists();
        }

        private ApplicationDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(_sqlConnectionString);
            var context = new ApplicationDbContext(optionsBuilder.Options);
            return context;
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
        /// Gets the Job ID for a given Job Instance ID.
        /// </summary>
        /// <param name="jobInstanceId">The job instance ID</param>
        /// <returns>The Job ID, or null if not found</returns>
        public async Task<int?> GetJobIdFromInstanceIdAsync(int jobInstanceId)
        {
            using var context = CreateDbContext();
            var instance = await context.JobInstances
                .Include(i => i.JobSchedule)
                .FirstOrDefaultAsync(i => i.Id == jobInstanceId);

            return instance?.JobSchedule?.JobId;
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
                }
                catch { /* Fail silently if logging fails */ }
            }

            // Log to Azure Table Storage with partition key "{JobId}-{JobInstanceId}"
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

        // #6 Create Designer Job Instance
        public async Task<int> CreateDesignerJobInstanceAsync(string jobName)
        {
            using var context = CreateDbContext();
            await LogAsync("CreateDesignerJobInstance", $"Creating designer instance for Job {jobName}");

            var result = context.Database.CanConnect();

            // 1. Create Job if needed
            var job = await context.Jobs.FirstOrDefaultAsync(j => j.JobName == jobName);
            if (job == null)
            {
                // Ensure Organization exists
                var org = await context.JobOrganizations.FirstOrDefaultAsync(o => o.OrganizationName == "Designer");
                if (org == null)
                {
                    org = new JobOrganization
                    {
                        OrganizationName = "Designer",
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = "System"
                    };
                    context.JobOrganizations.Add(org);
                    await context.SaveChangesAsync();
                }

                job = new Job
                {
                    JobName = jobName,
                    JobCodeFile = "Designer.cs", // Placeholder
                    JobEnvironment = "Designer",
                    JobOrganizationId = org.Id,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = "Designer"
                };
                context.Jobs.Add(job);
                await context.SaveChangesAsync();
                await LogAsync("CreateDesignerJobInstance", $"Created Job {jobName}");
            }

            // 2. Ensure Schedule exists
            var schedule = await context.JobSchedules.FirstOrDefaultAsync(s => s.JobId == job.Id && s.ScheduleName == "Designer");
            if (schedule == null)
            {
                schedule = new JobSchedule
                {
                    JobId = job.Id,
                    ScheduleName = "Designer",
                    Enabled = false,
                    InProcess = false,
                    HadError = false,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = "Designer"
                };
                context.JobSchedules.Add(schedule);
                await context.SaveChangesAsync();
                await LogAsync("CreateDesignerJobInstance", $"Created Schedule for Job {jobName}");
            }

            // 3. Create JobInstance
            var instance = new JobInstance
            {
                JobScheduleId = schedule.Id,
                AgentId = "-1",
                InProcess = true,
                HasError = false,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = "Designer"
            };
            context.JobInstances.Add(instance);
            await context.SaveChangesAsync();

            await LogAsync("CreateDesignerJobInstance", $"Created Instance {instance.Id} with ScheduleId {schedule.Id}");
            return instance.Id;
        }

        // #7 Complete Job Instance
        public async Task CompleteJobInstanceAsync(int jobInstanceId, bool hasError = false)
        {
            using var context = CreateDbContext();
            await LogAsync("CompleteJobInstance", $"Completing Instance {jobInstanceId} (Error: {hasError})");

            var instance = await context.JobInstances.FirstOrDefaultAsync(i => i.Id == jobInstanceId);
            if (instance != null)
            {
                instance.InProcess = false;
                instance.HasError = hasError;
                instance.UpdatedDate = DateTime.UtcNow;
                instance.UpdatedBy = "Designer";
                await context.SaveChangesAsync();
                await LogAsync("CompleteJobInstance", $"Instance {jobInstanceId} completed");
            }
            else
            {
                await LogAsync("CompleteJobInstance", $"Instance {jobInstanceId} not found", "Warning");
            }
        }

        // #8 Upload Job Package
        /// <summary>
        /// Uploads a NuGet package for a job to Azure Blob Storage.
        /// Updates the Job.JobCodeFile with the unique blob name.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <param name="fileStream">The file stream to upload</param>
        /// <param name="fileName">The original filename</param>
        /// <returns>The unique blob name stored in Job.JobCodeFile</returns>
        public async Task<string> UploadJobPackageAsync(int jobId, Stream fileStream, string fileName)
        {
            using var context = CreateDbContext();
            await LogAsync("UploadJobPackage", $"Uploading package for Job {jobId}: {fileName}", jobId: jobId);

            var job = await context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null)
            {
                throw new ArgumentException($"Job {jobId} not found.");
            }

            // Delete existing package if present
            if (!string.IsNullOrEmpty(job.JobCodeFile))
            {
                var existingBlob = _packageContainerClient.GetBlobClient(job.JobCodeFile);
                await existingBlob.DeleteIfExistsAsync();
                await LogAsync("UploadJobPackage", $"Deleted existing package: {job.JobCodeFile}", jobId: jobId);
            }

            // Generate unique filename: {JobId}_{Guid}_{timestamp}.nupkg
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".nupkg";
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var uniqueName = $"{jobId}_{Guid.NewGuid():N}_{timestamp}{extension}";

            // Upload to blob
            var blobClient = _packageContainerClient.GetBlobClient(uniqueName);
            
            // Reset stream position if possible
            if (fileStream.CanSeek)
            {
                fileStream.Position = 0;
            }

            await blobClient.UploadAsync(fileStream, new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "application/octet-stream"
            });

            // Update job record
            job.JobCodeFile = uniqueName;
            job.UpdatedDate = DateTime.UtcNow;
            job.UpdatedBy = "System";
            await context.SaveChangesAsync();

            await LogAsync("UploadJobPackage", $"Package uploaded: {uniqueName}", jobId: jobId);
            return uniqueName;
        }

        // #9 Run Job Now
        /// <summary>
        /// Triggers immediate execution of a job by creating a JobInstance and sending a queue message.
        /// </summary>
        /// <param name="jobId">The job ID to run</param>
        /// <returns>The created JobInstance ID</returns>
        public async Task<int> RunJobNowAsync(int jobId)
        {
            using var context = CreateDbContext();
            await LogAsync("RunJobNow", $"Triggering immediate run for Job {jobId}", jobId: jobId);

            var job = await context.Jobs
                .Include(j => j.JobSchedules)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job == null)
            {
                throw new ArgumentException($"Job {jobId} not found.");
            }

            // Get or create a schedule
            var schedule = job.JobSchedules.FirstOrDefault();
            if (schedule == null)
            {
                // Create a default "Run Now" schedule
                schedule = new JobSchedule
                {
                    JobId = jobId,
                    ScheduleName = "RunNow",
                    Enabled = false, // Not a recurring schedule
                    InProcess = false,
                    HadError = false,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = "System"
                };
                context.JobSchedules.Add(schedule);
                await context.SaveChangesAsync();
                await LogAsync("RunJobNow", $"Created default schedule for Job {jobId}", jobId: jobId);
            }

            // Create JobInstance
            var instance = new JobInstance
            {
                JobScheduleId = schedule.Id,
                InProcess = false, // Will be set to true when agent picks it up
                HasError = false,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = "System"
            };
            context.JobInstances.Add(instance);
            await context.SaveChangesAsync();

            await LogAsync("RunJobNow", $"Created JobInstance {instance.Id}", jobId: jobId, jobInstanceId: instance.Id);

            // Create and send queue message
            var queueMessage = new JobQueueMessage
            {
                JobInstanceId = instance.Id,
                JobId = jobId,
                QueuedAt = DateTime.UtcNow
            };

            var messageJson = JsonSerializer.Serialize(queueMessage);
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(messageJson);
            var base64Message = Convert.ToBase64String(messageBytes);

            await _jobQueueClient.SendMessageAsync(base64Message);

            await LogAsync("RunJobNow", $"Queue message sent for JobInstance {instance.Id}", jobId: jobId, jobInstanceId: instance.Id);
            return instance.Id;
        }

        // #10 Process Job Instance (called by Agent)
        /// <summary>
        /// Processes a job instance: downloads package, validates, executes code, logs results.
        /// This is the main entry point called by the Agent worker.
        /// </summary>
        /// <param name="jobInstanceId">The job instance ID to process</param>
        /// <param name="packageProcessor">The package processor service</param>
        /// <param name="codeExecutor">The code executor service</param>
        /// <param name="agentId">Optional agent identifier</param>
        public async Task ProcessJobInstanceAsync(
            int jobInstanceId,
            PackageProcessorService packageProcessor,
            CodeExecutorService codeExecutor,
            string? agentId = null)
        {
            using var context = CreateDbContext();
            await LogAsync("ProcessJobInstance", $"Processing JobInstance {jobInstanceId}", jobInstanceId: jobInstanceId);

            var instance = await context.JobInstances
                .Include(i => i.JobSchedule)
                .ThenInclude(s => s.Job)
                .FirstOrDefaultAsync(i => i.Id == jobInstanceId);

            if (instance == null)
            {
                await LogAsync("ProcessJobInstance", $"JobInstance {jobInstanceId} not found", "Error", jobInstanceId: jobInstanceId);
                return;
            }

            var job = instance.JobSchedule?.Job;
            if (job == null)
            {
                await LogAsync("ProcessJobInstance", $"Job not found for JobInstance {jobInstanceId}", "Error", jobInstanceId: jobInstanceId);
                return;
            }

            var jobId = job.Id;
            await LogAsync("ProcessJobInstance", $"Processing Job {job.JobName} (ID: {jobId})", jobId: jobId, jobInstanceId: jobInstanceId);

            // Mark as in process
            instance.InProcess = true;
            instance.AgentId = agentId;
            await context.SaveChangesAsync();

            string tempDir = Path.Combine(Path.GetTempPath(), "BlazorDataOrchestrator", Guid.NewGuid().ToString());

            try
            {
                // Check if job has a package
                if (string.IsNullOrEmpty(job.JobCodeFile))
                {
                    throw new InvalidOperationException($"Job {jobId} has no code package uploaded.");
                }

                await LogAsync("ProcessJobInstance", $"Downloading package: {job.JobCodeFile}", jobId: jobId, jobInstanceId: jobInstanceId);

                // Download and extract package
                var downloaded = await packageProcessor.DownloadAndExtractPackageAsync(job.JobCodeFile, tempDir);
                if (!downloaded)
                {
                    throw new FileNotFoundException($"Package {job.JobCodeFile} not found in blob storage.");
                }

                await LogAsync("ProcessJobInstance", $"Package extracted to {tempDir}", jobId: jobId, jobInstanceId: jobInstanceId);

                // Validate package structure
                var validation = await packageProcessor.ValidateNuSpecAsync(tempDir);
                if (!validation.IsValid)
                {
                    var errors = string.Join("; ", validation.Errors);
                    throw new InvalidOperationException($"Package validation failed: {errors}");
                }

                foreach (var warning in validation.Warnings)
                {
                    await LogAsync("ProcessJobInstance", $"Warning: {warning}", "Warning", jobId: jobId, jobInstanceId: jobInstanceId);
                }

                // Get configuration
                var config = await packageProcessor.GetConfigurationAsync(tempDir);
                await LogAsync("ProcessJobInstance", $"Selected language: {config.SelectedLanguage}", jobId: jobId, jobInstanceId: jobInstanceId);

                // Build app settings JSON with connection strings
                var appSettings = new
                {
                    ConnectionStrings = new Dictionary<string, string>
                    {
                        { "blazororchestratordb", _sqlConnectionString },
                        { "tables", _tableConnectionString }
                    }
                };
                var appSettingsJson = JsonSerializer.Serialize(appSettings);

                // Create execution context
                var executionContext = new JobExecutionContext
                {
                    JobId = jobId,
                    JobInstanceId = jobInstanceId,
                    JobScheduleId = instance.JobScheduleId,
                    SelectedLanguage = config.SelectedLanguage,
                    AppSettingsJson = appSettingsJson,
                    SqlConnectionString = _sqlConnectionString,
                    TableConnectionString = _tableConnectionString,
                    AgentId = agentId
                };

                // Execute code
                await LogAsync("ProcessJobInstance", "Starting code execution...", jobId: jobId, jobInstanceId: jobInstanceId);
                var result = await codeExecutor.ExecuteAsync(tempDir, executionContext);

                // Log execution results
                foreach (var log in result.Logs)
                {
                    await LogAsync("JobExecution", log, jobId: jobId, jobInstanceId: jobInstanceId);
                }

                if (result.Success)
                {
                    await LogAsync("ProcessJobInstance", $"Job completed successfully in {result.Duration.TotalSeconds:F2} seconds", jobId: jobId, jobInstanceId: jobInstanceId);
                    instance.HasError = false;
                }
                else
                {
                    await LogAsync("ProcessJobInstance", $"Job failed: {result.ErrorMessage}", "Error", jobId: jobId, jobInstanceId: jobInstanceId);
                    if (!string.IsNullOrEmpty(result.StackTrace))
                    {
                        await LogAsync("ProcessJobInstance", $"Stack trace: {result.StackTrace}", "Error", jobId: jobId, jobInstanceId: jobInstanceId);
                    }
                    instance.HasError = true;
                }

                instance.InProcess = false;
                instance.UpdatedDate = DateTime.UtcNow;
                instance.UpdatedBy = agentId ?? "Agent";
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                await LogAsync("ProcessJobInstance", $"Error: {ex.Message}", "Error", jobId: jobId, jobInstanceId: jobInstanceId);
                
                instance.InProcess = false;
                instance.HasError = true;
                instance.UpdatedDate = DateTime.UtcNow;
                instance.UpdatedBy = agentId ?? "Agent";
                await context.SaveChangesAsync();

                throw; // Re-throw so caller can handle
            }
            finally
            {
                // Clean up temp directory
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        // #11 Get Job Details
        /// <summary>
        /// Gets a job with its schedules and parameters.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <returns>The job with related data, or null if not found</returns>
        public async Task<Job?> GetJobAsync(int jobId)
        {
            using var context = CreateDbContext();
            return await context.Jobs
                .Include(j => j.JobSchedules)
                .Include(j => j.JobData)
                .Include(j => j.JobOrganization)
                .FirstOrDefaultAsync(j => j.Id == jobId);
        }

        // #12 Update Job Code File
        /// <summary>
        /// Updates just the JobCodeFile field for a job.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <param name="codeFileName">The new code file name (blob name)</param>
        public async Task UpdateJobCodeFileAsync(int jobId, string codeFileName)
        {
            using var context = CreateDbContext();
            var job = await context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (job != null)
            {
                job.JobCodeFile = codeFileName;
                job.UpdatedDate = DateTime.UtcNow;
                job.UpdatedBy = "System";
                await context.SaveChangesAsync();
                await LogAsync("UpdateJobCodeFile", $"Updated JobCodeFile to {codeFileName}", jobId: jobId);
            }
        }
    }
}
