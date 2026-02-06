
using Microsoft.EntityFrameworkCore;
using BlazorOrchestrator.Scheduler.Models;

namespace BlazorOrchestrator.Scheduler.Data;

public class SchedulerDbContext : DbContext
{
    public SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : base(options)
    {
    }

    public DbSet<JobData> JobData { get; set; }
    public DbSet<JobGroups> JobGroups { get; set; }
    public DbSet<JobInstance> JobInstance { get; set; }
    public DbSet<JobJobGroup> JobJobGroup { get; set; }
    public DbSet<JobOrganizations> JobOrganizations { get; set; }
    public DbSet<JobQueue> JobQueue { get; set; }
    public DbSet<JobSchedule> JobSchedule { get; set; }
    public DbSet<Jobs> Jobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map to actual table names
        modelBuilder.Entity<JobJobGroup>().ToTable("Job_JobGroup");

        // Configure Jobs entity
        modelBuilder.Entity<Jobs>(entity =>
        {
            // Map JobQueueNavigation to the JobQueue column as foreign key
            entity.HasOne(d => d.JobQueueNavigation)
                .WithMany(p => p.Jobs)
                .HasForeignKey(d => d.JobQueue)
                .HasConstraintName("FK_Jobs_JobQueue");

            // Map JobOrganization relationship
            entity.HasOne(d => d.JobOrganization)
                .WithMany(p => p.Jobs)
                .HasForeignKey(d => d.JobOrganizationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs_JobOrganizations");
        });
    }
}