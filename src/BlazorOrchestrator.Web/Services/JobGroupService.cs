using BlazorOrchestrator.Web.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Services;

public class JobGroupService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<JobGroupService> _logger;

    public JobGroupService(ApplicationDbContext dbContext, ILogger<JobGroupService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // Job Group CRUD Operations

    /// <summary>
    /// Gets all job groups.
    /// </summary>
    public async Task<List<JobGroup>> GetJobGroupsAsync()
    {
        return await _dbContext.JobGroups
            .OrderBy(g => g.JobGroupName)
            .ToListAsync();
    }

    /// <summary>
    /// Gets only active job groups (for filtering and selection purposes).
    /// </summary>
    public async Task<List<JobGroup>> GetActiveJobGroupsAsync()
    {
        return await _dbContext.JobGroups
            .Where(g => g.IsActive)
            .OrderBy(g => g.JobGroupName)
            .ToListAsync();
    }

    /// <summary>
    /// Gets a job group by its ID.
    /// </summary>
    public async Task<JobGroup?> GetJobGroupByIdAsync(int id)
    {
        return await _dbContext.JobGroups
            .Include(g => g.JobJobGroups)
                .ThenInclude(jjg => jjg.Job)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    /// <summary>
    /// Creates a new job group.
    /// </summary>
    public async Task<JobGroup> CreateJobGroupAsync(string name)
    {
        var jobGroup = new JobGroup
        {
            JobGroupName = name,
            IsActive = true,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "System"
        };

        _dbContext.JobGroups.Add(jobGroup);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created job group '{JobGroupName}' with ID {JobGroupId}", name, jobGroup.Id);
        return jobGroup;
    }

    /// <summary>
    /// Updates an existing job group.
    /// </summary>
    public async Task<JobGroup> UpdateJobGroupAsync(JobGroup jobGroup)
    {
        jobGroup.UpdatedDate = DateTime.UtcNow;
        jobGroup.UpdatedBy = "System";

        _dbContext.JobGroups.Update(jobGroup);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Updated job group '{JobGroupName}' with ID {JobGroupId}", jobGroup.JobGroupName, jobGroup.Id);
        return jobGroup;
    }

    /// <summary>
    /// Deletes a job group and its associations.
    /// </summary>
    public async Task DeleteJobGroupAsync(int id)
    {
        var jobGroup = await _dbContext.JobGroups
            .Include(g => g.JobJobGroups)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (jobGroup != null)
        {
            // Remove all job-group associations first
            _dbContext.JobJobGroups.RemoveRange(jobGroup.JobJobGroups);
            
            // Then remove the group itself
            _dbContext.JobGroups.Remove(jobGroup);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Deleted job group with ID {JobGroupId}", id);
        }
    }

    // Job-to-Group Assignment Operations

    /// <summary>
    /// Assigns a job to a job group.
    /// </summary>
    public async Task AssignJobToGroupAsync(int jobId, int groupId)
    {
        // Check if assignment already exists
        var existingAssignment = await _dbContext.JobJobGroups
            .FirstOrDefaultAsync(jjg => jjg.JobId == jobId && jjg.JobGroupId == groupId);

        if (existingAssignment != null)
        {
            _logger.LogDebug("Job {JobId} is already assigned to group {GroupId}", jobId, groupId);
            return;
        }

        var assignment = new JobJobGroup
        {
            JobId = jobId,
            JobGroupId = groupId
        };

        _dbContext.JobJobGroups.Add(assignment);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Assigned job {JobId} to group {GroupId}", jobId, groupId);
    }

    /// <summary>
    /// Removes a job from a job group.
    /// </summary>
    public async Task RemoveJobFromGroupAsync(int jobId, int groupId)
    {
        var assignment = await _dbContext.JobJobGroups
            .FirstOrDefaultAsync(jjg => jjg.JobId == jobId && jjg.JobGroupId == groupId);

        if (assignment != null)
        {
            _dbContext.JobJobGroups.Remove(assignment);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Removed job {JobId} from group {GroupId}", jobId, groupId);
        }
    }

    /// <summary>
    /// Gets all job groups that a specific job belongs to.
    /// </summary>
    public async Task<List<JobGroup>> GetJobGroupsForJobAsync(int jobId)
    {
        return await _dbContext.JobJobGroups
            .Where(jjg => jjg.JobId == jobId)
            .Include(jjg => jjg.JobGroup)
            .Select(jjg => jjg.JobGroup)
            .OrderBy(g => g.JobGroupName)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all jobs in a specific job group.
    /// </summary>
    public async Task<List<Job>> GetJobsInGroupAsync(int groupId)
    {
        return await _dbContext.JobJobGroups
            .Where(jjg => jjg.JobGroupId == groupId)
            .Include(jjg => jjg.Job)
            .Select(jjg => jjg.Job)
            .OrderBy(j => j.JobName)
            .ToListAsync();
    }

    /// <summary>
    /// Updates all group assignments for a job (replaces existing assignments).
    /// </summary>
    public async Task UpdateJobGroupAssignmentsAsync(int jobId, IEnumerable<int> groupIds)
    {
        // Remove existing assignments
        var existingAssignments = await _dbContext.JobJobGroups
            .Where(jjg => jjg.JobId == jobId)
            .ToListAsync();

        _dbContext.JobJobGroups.RemoveRange(existingAssignments);

        // Add new assignments
        foreach (var groupId in groupIds)
        {
            _dbContext.JobJobGroups.Add(new JobJobGroup
            {
                JobId = jobId,
                JobGroupId = groupId
            });
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Updated job group assignments for job {JobId} with groups [{GroupIds}]", 
            jobId, string.Join(", ", groupIds));
    }
}
