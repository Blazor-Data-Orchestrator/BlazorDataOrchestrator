using Microsoft.EntityFrameworkCore;

namespace BlazorOrchestrator.Web.Data.Data;

public partial class ApplicationDbContext
{
    public static ApplicationDbContext Create(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
