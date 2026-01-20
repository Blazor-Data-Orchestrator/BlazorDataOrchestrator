using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Data.Data;

/// <summary>
/// Partial class to extend the auto-generated ApplicationDbContext with custom configurations.
/// This ensures cascade delete behavior for Job-related entities.
/// </summary>
public partial class ApplicationDbContext
{
    /// <summary>
    /// Extends the model configuration to set up cascade delete behavior for Job relationships.
    /// This is called after OnModelCreating completes.
    /// </summary>
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // Configure Job → JobSchedule: Cascade delete
        // When a Job is deleted, all related JobSchedules should also be deleted
        modelBuilder.Entity<JobSchedule>(entity =>
        {
            entity.HasOne(d => d.Job)
                .WithMany(p => p.JobSchedules)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure JobSchedule → JobInstance: Cascade delete
        // When a JobSchedule is deleted, all related JobInstances should also be deleted
        modelBuilder.Entity<JobInstance>(entity =>
        {
            entity.HasOne(d => d.JobSchedule)
                .WithMany(p => p.JobInstances)
                .HasForeignKey(d => d.JobScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Job → JobData: Cascade delete
        // When a Job is deleted, all related JobData should also be deleted
        modelBuilder.Entity<JobDatum>(entity =>
        {
            entity.HasOne(d => d.Job)
                .WithMany(p => p.JobData)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Job → JobJobGroup: Cascade delete
        // When a Job is deleted, all related JobJobGroup mappings should also be deleted
        modelBuilder.Entity<JobJobGroup>(entity =>
        {
            entity.HasOne(d => d.Job)
                .WithMany(p => p.JobJobGroups)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
