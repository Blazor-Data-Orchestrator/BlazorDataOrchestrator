using Microsoft.EntityFrameworkCore;

namespace BlazorOrchistrator.Agent.Data;

public class AgentDbContext : DbContext
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options)
    {
    }

    // Add your DbSets here as needed
    // public DbSet<AgentTask> AgentTasks { get; set; }
}