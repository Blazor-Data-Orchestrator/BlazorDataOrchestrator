using Microsoft.EntityFrameworkCore;

namespace BlazorOrchistrator.Web.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // Add your DbSets here as needed
    // public DbSet<YourEntity> YourEntities { get; set; }
}