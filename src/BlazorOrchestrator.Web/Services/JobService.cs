using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

public class JobService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<JobService> _logger;

    public JobService(ApplicationDbContext dbContext, ILogger<JobService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // Job CRUD Operations
    public async Task<List<Job>> GetJobsAsync(int? organizationId = null)
    {
        var query = _dbContext.Jobs.AsQueryable();
        
        if (organizationId.HasValue)
        {
            query = query.Where(j => j.JobOrganizationId == organizationId.Value);
        }

        return await query
            .Include(j => j.JobSchedules)
            .Include(j => j.JobData)
            .OrderBy(j => j.JobName)
            .ToListAsync();
    }

    public async Task<Job?> GetJobByIdAsync(int jobId)
    {
        return await _dbContext.Jobs
            .Include(j => j.JobSchedules)
            .Include(j => j.JobData)
            .Include(j => j.JobOrganization)
            .FirstOrDefaultAsync(j => j.Id == jobId);
    }

    public async Task<Job> CreateJobAsync(string jobName, int organizationId, string? environment = null)
    {
        // Get the default queue - look for "default" queue by name
        var defaultQueue = await _dbContext.JobQueues
            .FirstOrDefaultAsync(q => q.QueueName.ToLower() == "default");
        
        var job = new Job
        {
            JobName = jobName,
            JobOrganizationId = organizationId,
            JobEnvironment = environment ?? "Development",
            JobEnabled = false,
            JobQueued = false,
            JobInProcess = false,
            JobInError = false,
            JobCodeFile = string.Empty, // Required field - will be populated later when job code is uploaded
            JobQueue = defaultQueue?.Id, // Set default queue if available
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "System"
        };

        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Created job '{JobName}' with ID {JobId} with default queue {QueueId}", jobName, job.Id, job.JobQueue);
        return job;
    }

    public async Task<Job> UpdateJobAsync(Job job)
    {
        job.UpdatedDate = DateTime.UtcNow;
        job.UpdatedBy = "System";
        
        _dbContext.Jobs.Update(job);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Updated job '{JobName}' with ID {JobId}", job.JobName, job.Id);
        return job;
    }

    public async Task DeleteJobAsync(int jobId)
    {
        var job = await _dbContext.Jobs
            .Include(j => j.JobSchedules)
                .ThenInclude(s => s.JobInstances)
            .Include(j => j.JobData)
            .Include(j => j.JobJobGroups)
            .FirstOrDefaultAsync(j => j.Id == jobId);
            
        if (job != null)
        {
            // Remove related entities explicitly (belt and suspenders with cascade delete)
            foreach (var schedule in job.JobSchedules.ToList())
            {
                _dbContext.JobInstances.RemoveRange(schedule.JobInstances);
                _dbContext.JobSchedules.Remove(schedule);
            }
            _dbContext.JobData.RemoveRange(job.JobData);
            _dbContext.JobJobGroups.RemoveRange(job.JobJobGroups);
            
            _dbContext.Jobs.Remove(job);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Deleted job with ID {JobId} and all related entities", jobId);
        }
    }

    // Job Schedule CRUD Operations
    public async Task<List<JobSchedule>> GetJobSchedulesAsync(int jobId)
    {
        return await _dbContext.JobSchedules
            .Where(s => s.JobId == jobId)
            .OrderBy(s => s.ScheduleName)
            .ToListAsync();
    }

    public async Task<JobSchedule?> GetJobScheduleByIdAsync(int scheduleId)
    {
        return await _dbContext.JobSchedules
            .Include(s => s.Job)
            .FirstOrDefaultAsync(s => s.Id == scheduleId);
    }

    public async Task<JobSchedule> CreateJobScheduleAsync(JobSchedule schedule)
    {
        schedule.CreatedDate = DateTime.UtcNow;
        schedule.CreatedBy = "System";
        
        _dbContext.JobSchedules.Add(schedule);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Created schedule '{ScheduleName}' for job ID {JobId}", schedule.ScheduleName, schedule.JobId);
        return schedule;
    }

    public async Task<JobSchedule> UpdateJobScheduleAsync(JobSchedule schedule)
    {
        schedule.UpdatedDate = DateTime.UtcNow;
        schedule.UpdatedBy = "System";
        
        _dbContext.JobSchedules.Update(schedule);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Updated schedule '{ScheduleName}' with ID {ScheduleId}", schedule.ScheduleName, schedule.Id);
        return schedule;
    }

    public async Task DeleteJobScheduleAsync(int scheduleId)
    {
        var schedule = await _dbContext.JobSchedules.FindAsync(scheduleId);
        if (schedule != null)
        {
            _dbContext.JobSchedules.Remove(schedule);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Deleted schedule with ID {ScheduleId}", scheduleId);
        }
    }

    // Job Data (Parameters) CRUD Operations
    public async Task<List<JobDatum>> GetJobParametersAsync(int jobId)
    {
        return await _dbContext.JobData
            .Where(d => d.JobId == jobId)
            .OrderBy(d => d.JobFieldDescription)
            .ToListAsync();
    }

    public async Task<JobDatum> CreateJobParameterAsync(JobDatum parameter)
    {
        parameter.CreatedDate = DateTime.UtcNow;
        parameter.CreatedBy = "System";
        
        _dbContext.JobData.Add(parameter);
        await _dbContext.SaveChangesAsync();
        
        return parameter;
    }

    public async Task<JobDatum> UpdateJobParameterAsync(JobDatum parameter)
    {
        parameter.UpdatedDate = DateTime.UtcNow;
        parameter.UpdatedBy = "System";
        
        _dbContext.JobData.Update(parameter);
        await _dbContext.SaveChangesAsync();
        
        return parameter;
    }

    public async Task DeleteJobParameterAsync(int parameterId)
    {
        var parameter = await _dbContext.JobData.FindAsync(parameterId);
        if (parameter != null)
        {
            _dbContext.JobData.Remove(parameter);
            await _dbContext.SaveChangesAsync();
        }
    }

    // Job Organizations
    public async Task<List<JobOrganization>> GetOrganizationsAsync()
    {
        return await _dbContext.JobOrganizations
            .OrderBy(o => o.OrganizationName)
            .ToListAsync();
    }

    public async Task<JobOrganization?> GetDefaultOrganizationAsync()
    {
        return await _dbContext.JobOrganizations.FirstOrDefaultAsync();
    }

    // Environment options
    public List<string> GetEnvironments()
    {
        return new List<string> { "Development", "Staging", "Production" };
    }

    // Job Instances (Logs)
    public async Task<List<JobInstance>> GetJobInstancesAsync(int jobId)
    {
        return await _dbContext.JobInstances
            .Include(i => i.JobSchedule)
            .Where(i => i.JobSchedule.JobId == jobId)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }
}
