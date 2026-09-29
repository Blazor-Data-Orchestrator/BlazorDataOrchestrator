using BlazorDataOrchestrator.Core.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class DesignerJobResolutionTests
{
    [Fact(DisplayName = "O1: the preferred job ID from configuration.json is reused")]
    [Trait("Category", "Contract")]
    public async Task CreateDesignerJobInstance_PreferredJobId_ReusesJob()
    {
        await using var context = CreateContext();
        var platformJob = await AddJobAsync(context, "Weather Report");

        var (job, created) = await JobManager.ResolveDesignerJobAsync(context, "BlazorDataOrchestrator.SomethingElse", platformJob.Id);

        Assert.False(created);
        Assert.Equal(platformJob.Id, job.Id);
        Assert.Equal(1, await context.Jobs.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "O1: a prefixed designer name matches the unprefixed platform job")]
    [Trait("Category", "Contract")]
    public async Task CreateDesignerJobInstance_PrefixedName_MatchesUnprefixedJob()
    {
        await using var context = CreateContext();
        var platformJob = await AddJobAsync(context, "WeatherJob");

        // A stale preferred ID (job deleted, or another database) falls through to name matching.
        var (job, created) = await JobManager.ResolveDesignerJobAsync(context, "BlazorDataOrchestrator.WeatherJob", 999);

        Assert.False(created);
        Assert.Equal(platformJob.Id, job.Id);
        Assert.Equal(1, await context.Jobs.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "O1: an exact name match wins over the unprefixed name")]
    [Trait("Category", "Contract")]
    public async Task CreateDesignerJobInstance_ExactName_Preferred()
    {
        await using var context = CreateContext();
        await AddJobAsync(context, "WeatherJob");
        var legacy = await AddJobAsync(context, "BlazorDataOrchestrator.WeatherJob");

        var (job, created) = await JobManager.ResolveDesignerJobAsync(context, "BlazorDataOrchestrator.WeatherJob", 0);

        Assert.False(created);
        Assert.Equal(legacy.Id, job.Id);
    }

    [Fact(DisplayName = "O1: with no match, a job with the unprefixed name is created")]
    [Trait("Category", "Contract")]
    public async Task CreateDesignerJobInstance_NoMatch_CreatesUnprefixedName()
    {
        await using var context = CreateContext();

        var (job, created) = await JobManager.ResolveDesignerJobAsync(context, "BlazorDataOrchestrator.NewJob", 0);

        Assert.True(created);
        Assert.Equal("NewJob", job.JobName);
        var stored = await context.Jobs.Include(j => j.JobOrganization).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Designer", stored.JobOrganization.OrganizationName);
    }

    [Theory(DisplayName = "O1: only a leading BlazorDataOrchestrator. prefix is stripped")]
    [Trait("Category", "Contract")]
    [InlineData("BlazorDataOrchestrator.Weather", "Weather")]
    [InlineData("Weather", "Weather")]
    [InlineData("BlazorDataOrchestrator.", "BlazorDataOrchestrator.")]
    [InlineData("My.BlazorDataOrchestrator.Weather", "My.BlazorDataOrchestrator.Weather")]
    public void StripDesignerPrefix_RemovesOnlyLeadingPrefix(string input, string expected)
    {
        Assert.Equal(expected, JobManager.StripDesignerPrefix(input));
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"designer-{Guid.NewGuid():N}")
            .Options);

    private static async Task<Job> AddJobAsync(ApplicationDbContext context, string name)
    {
        var org = await context.JobOrganizations.FirstOrDefaultAsync(o => o.OrganizationName == "Default");
        if (org == null)
        {
            org = new JobOrganization { OrganizationName = "Default", CreatedDate = DateTime.UtcNow, CreatedBy = "Test" };
            context.JobOrganizations.Add(org);
            await context.SaveChangesAsync();
        }

        var job = new Job
        {
            JobName = name,
            JobEnvironment = "Production",
            JobCodeFile = "job.nupkg",
            JobOrganizationId = org.Id,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "Test"
        };
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        return job;
    }
}
