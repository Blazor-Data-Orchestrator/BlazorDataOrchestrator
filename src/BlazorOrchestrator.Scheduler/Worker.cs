using BlazorOrchestrator.Scheduler.Data;
using BlazorOrchestrator.Scheduler.Models;
using BlazorOrchestrator.Scheduler.Services;
using BlazorOrchestrator.Scheduler.Settings;
using Microsoft.Extensions.Options;

namespace BlazorOrchestrator.Scheduler;

/// <summary>
/// Background service that schedules jobs based on their configured schedules
/// and enqueues them to Azure Storage Queues for processing by agents.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SchedulerSettings _settings;

    public Worker(
        ILogger<Worker> logger,
        IServiceProvider serviceProvider,
        IOptions<SchedulerSettings> settings)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler Worker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledJobsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Scheduler Worker stopping...");
    }

    /// <summary>
    /// Main scheduling logic - queries schedules, marks stuck instances, and enqueues due jobs.
    /// </summary>
    private async Task ProcessScheduledJobsAsync()
    {
        if (_settings.VerboseLogging)
        {
            _logger.LogInformation("Scheduler running at: {time}", DateTimeOffset.Now);
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        var queueService = scope.ServiceProvider.GetRequiredService<IJobQueueService>();

        var now = DateTime.UtcNow;
        var weekday = now.DayOfWeek;
        var timeNow = (now.Hour * 100) + now.Minute; // Military time format

        // Step 1: Mark stuck job instances as error
        await MarkStuckJobInstancesAsync(dbContext, now);

        // Step 2: Query enabled job schedules
        var schedules = dbContext.JobSchedule
            .Where(s => s.Enabled)
            .ToList();

        if (_settings.VerboseLogging)
        {
            _logger.LogInformation("Found {Count} enabled schedules to evaluate", schedules.Count);
        }

        // Step 3: Process each schedule
        foreach (var schedule in schedules)
        {
            await ProcessScheduleAsync(dbContext, queueService, schedule, now, weekday, timeNow);
        }
    }

    /// <summary>
    /// Marks job instances as error if they haven't been updated within the timeout period.
    /// </summary>
    private async Task MarkStuckJobInstancesAsync(SchedulerDbContext dbContext, DateTime now)
    {
        var cutoff = now.AddHours(-_settings.StuckJobTimeoutHours);
        var stuckInstances = dbContext.JobInstance
            .Where(i => i.UpdatedDate == null && i.CreatedDate <= cutoff && !i.HasError)
            .ToList();

        if (stuckInstances.Count == 0) return;

        foreach (var stuck in stuckInstances)
        {
            stuck.HasError = true;
            stuck.UpdatedDate = now;
            stuck.UpdatedBy = "Scheduler";

            _logger.LogWarning("JobInstance {JobInstanceId} marked as error due to timeout ({TimeoutHours} hours without update)",
                stuck.Id, _settings.StuckJobTimeoutHours);
        }

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Processes a single schedule to determine if it should be executed.
    /// </summary>
    private async Task ProcessScheduleAsync(
        SchedulerDbContext dbContext,
        IJobQueueService queueService,
        JobSchedule schedule,
        DateTime now,
        DayOfWeek weekday,
        int timeNow)
    {
        // Check if today is a scheduled day
        if (!IsTodayScheduled(schedule, weekday))
        {
            if (_settings.VerboseLogging)
            {
                _logger.LogDebug("Schedule {ScheduleId} not scheduled for {Day}", schedule.Id, weekday);
            }
            return;
        }

        // Check if within time window
        if (!IsWithinTimeWindow(schedule, timeNow))
        {
            if (_settings.VerboseLogging)
            {
                _logger.LogDebug("Schedule {ScheduleId} outside time window ({TimeNow} not in {Start}-{Stop})",
                    schedule.Id, timeNow, schedule.StartTime, schedule.StopTime);
            }
            return;
        }

        // Check if there's already an instance in process
        var inProcess = dbContext.JobInstance.Any(i =>
            i.JobScheduleId == schedule.Id && i.InProcess && !i.HasError);

        if (inProcess)
        {
            if (_settings.VerboseLogging)
            {
                _logger.LogDebug("Schedule {ScheduleId} already has an instance in process", schedule.Id);
            }
            return;
        }

        // Check if should schedule based on run interval
        if (!ShouldScheduleJob(dbContext, schedule, now))
        {
            return;
        }

        // Create and enqueue the job
        await CreateAndEnqueueJobAsync(dbContext, queueService, schedule, now);
    }

    /// <summary>
    /// Checks if the current day matches the schedule's day configuration.
    /// </summary>
    private static bool IsTodayScheduled(JobSchedule schedule, DayOfWeek weekday)
    {
        return weekday switch
        {
            DayOfWeek.Monday => schedule.Monday,
            DayOfWeek.Tuesday => schedule.Tuesday,
            DayOfWeek.Wednesday => schedule.Wednesday,
            DayOfWeek.Thursday => schedule.Thursday,
            DayOfWeek.Friday => schedule.Friday,
            DayOfWeek.Saturday => schedule.Saturday,
            DayOfWeek.Sunday => schedule.Sunday,
            _ => false
        };
    }

    /// <summary>
    /// Checks if the current time is within the schedule's time window.
    /// </summary>
    private static bool IsWithinTimeWindow(JobSchedule schedule, int timeNow)
    {
        if (!schedule.StartTime.HasValue || !schedule.StopTime.HasValue)
        {
            return true; // No time restriction
        }

        return timeNow >= schedule.StartTime.Value && timeNow <= schedule.StopTime.Value;
    }

    /// <summary>
    /// Determines if a job should be scheduled based on the last run and interval.
    /// </summary>
    private bool ShouldScheduleJob(SchedulerDbContext dbContext, JobSchedule schedule, DateTime now)
    {
        var lastInstance = dbContext.JobInstance
            .Where(i => i.JobScheduleId == schedule.Id)
            .OrderByDescending(i => i.CreatedDate)
            .FirstOrDefault();

        if (lastInstance == null)
        {
            return true; // Never run before
        }

        if (!lastInstance.UpdatedDate.HasValue)
        {
            // Last instance hasn't completed yet (but not marked as stuck)
            return false;
        }

        if (!schedule.RunEveryHour.HasValue)
        {
            return true; // No interval restriction
        }

        var elapsedMinutes = (now - lastInstance.UpdatedDate.Value).TotalMinutes;
        var intervalMinutes = schedule.RunEveryHour.Value * 60;

        return elapsedMinutes >= intervalMinutes;
    }

    /// <summary>
    /// Creates a job instance and enqueues it to the appropriate queue.
    /// </summary>
    private async Task CreateAndEnqueueJobAsync(
        SchedulerDbContext dbContext,
        IJobQueueService queueService,
        JobSchedule schedule,
        DateTime now)
    {
        // Get the job to determine queue
        var job = dbContext.Jobs.FirstOrDefault(j => j.Id == schedule.JobId);

        if (job == null)
        {
            _logger.LogError("Job not found for schedule {ScheduleId} with JobId {JobId}",
                schedule.Id, schedule.JobId);
            return;
        }

        if (!job.JobEnabled)
        {
            if (_settings.VerboseLogging)
            {
                _logger.LogDebug("Job {JobId} is disabled, skipping schedule {ScheduleId}",
                    job.Id, schedule.Id);
            }
            return;
        }

        // Resolve queue name from JobQueue table
        string queueName = _settings.DefaultQueueName;
        if (job.JobQueue.HasValue)
        {
            var jobQueue = dbContext.JobQueue.FirstOrDefault(q => q.Id == job.JobQueue.Value);
            if (jobQueue != null)
            {
                queueName = jobQueue.QueueName;
            }
            else
            {
                _logger.LogWarning("JobQueue with Id {QueueId} not found for Job {JobId}, using default queue",
                    job.JobQueue.Value, job.Id);
            }
        }

        // Create JobInstance
        var jobInstance = new JobInstance
        {
            JobScheduleId = schedule.Id,
            CreatedDate = now,
            CreatedBy = "Scheduler",
            InProcess = true,
            HasError = false
        };

        dbContext.JobInstance.Add(jobInstance);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Created JobInstance {JobInstanceId} for Job '{JobName}' (Schedule: {ScheduleId})",
            jobInstance.Id, job.JobName, schedule.Id);

        // Enqueue to Azure Queue
        var success = await queueService.EnqueueJobAsync(jobInstance.Id, job.Id, queueName);

        if (success)
        {
            // Mark job as queued
            job.JobQueued = true;
            job.UpdatedDate = now;
            job.UpdatedBy = "Scheduler";

            _logger.LogInformation("Enqueued JobInstance {JobInstanceId} to queue '{QueueName}'",
                jobInstance.Id, queueName);
        }
        else
        {
            // Mark instance as error
            jobInstance.HasError = true;
            jobInstance.InProcess = false;
            jobInstance.UpdatedDate = now;
            jobInstance.UpdatedBy = "Scheduler";

            _logger.LogError("Failed to enqueue JobInstance {JobInstanceId} to queue '{QueueName}'",
                jobInstance.Id, queueName);
        }

        await dbContext.SaveChangesAsync();
    }
}
