using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Scheduler.Data;

public class SchedulerDbContext : DbContext
{
    public SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : base(options)
    {
    }

    // Add your DbSets here as needed
    // public DbSet<ScheduledJob> ScheduledJobs { get; set; }
}