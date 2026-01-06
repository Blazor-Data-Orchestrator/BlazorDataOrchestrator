using Microsoft.EntityFrameworkCore;

namespace BlazorDataOrchestrator.Core.Data;

public partial class ApplicationDbContext
{
    public static ApplicationDbContext Create(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
