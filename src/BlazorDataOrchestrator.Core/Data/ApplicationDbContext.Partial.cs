using Microsoft.EntityFrameworkCore;

namespace BlazorDataOrchestrator.Core.Data;

public partial class ApplicationDbContext
{
    /// <summary>
    /// DbSet for JobQueue entities (added for environment-specific queueing feature).
    /// </summary>
    public virtual DbSet<JobQueue> JobQueues { get; set; }

    public static ApplicationDbContext Create(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // Configure JobQueue entity
        modelBuilder.Entity<JobQueue>(entity =>
        {
            entity.ToTable("JobQueue");

            entity.Property(e => e.QueueName)
                .IsRequired()
                .HasMaxLength(250);

            entity.Property(e => e.CreatedBy)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.CreatedDate)
                .HasColumnType("datetime");
        });

        // Configure Job -> JobQueue relationship
        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasOne(d => d.JobQueueNavigation)
                .WithMany(p => p.Jobs)
                .HasForeignKey(d => d.JobQueue)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Jobs_JobQueue");
        });
    }
}
