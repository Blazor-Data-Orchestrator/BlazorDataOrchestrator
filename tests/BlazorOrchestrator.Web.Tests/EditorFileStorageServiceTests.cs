using BlazorDataOrchestrator.Core.Configuration;
using BlazorOrchestrator.Web.Services;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

public class EditorFileStorageServiceTests
{
    [Fact(DisplayName = "Web editor preserves source, environments, dependencies, and nuspec")]
    [Trait("Category", "Integration")]
    public void InitializeAndReloadPreservesTheCompleteEditorContract()
    {
        const int jobId = 42;
        var service = new EditorFileStorageService();
        var model = new JobCodeModel
        {
            Language = "csharp",
            MainCode = "public class BlazorDataOrchestratorJob {}",
            AppSettings = "{}",
            NuspecContent = "<package />",
            NuspecFileName = "job.nuspec",
            Dependencies = [new NuGetDependencyInfo { PackageId = "Humanizer.Core", Version = "2.14.1", TargetFramework = "net10.0" }],
            EnvironmentAppSettings = JobEnvironments.All.ToDictionary(environment => environment, environment => $"{{\"Environment\":\"{environment}\"}}")
        };

        service.InitializeFromCodeModel(jobId, model);

        Assert.Equal(model.MainCode, service.GetFile(jobId, "main.cs"));
        Assert.Equal(JobEnvironments.AllFileNames.Order(), service.GetFileNames(jobId).Where(name => name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)).Order());
        Assert.Equal("Humanizer.Core", Assert.Single(service.GetDependencies(jobId)).PackageId);
        Assert.Equal((model.NuspecContent, model.NuspecFileName), service.GetNuspecInfo(jobId));
    }

    [Fact(DisplayName = "Web editor clear removes all job-owned state")]
    [Trait("Category", "Integration")]
    public void ClearFilesRemovesFilesDependenciesAndNuspec()
    {
        var service = new EditorFileStorageService();
        service.SetFile(7, "main.py", "pass");
        service.SetDependencies(7, [new NuGetDependencyInfo { PackageId = "Example", Version = "1.0.0" }]);
        service.SetNuspecContent(7, "<package />", "job.nuspec");

        service.ClearFiles(7);

        Assert.Empty(service.GetAllFiles(7));
        Assert.Empty(service.GetDependencies(7));
        Assert.Equal((null, null), service.GetNuspecInfo(7));
    }
}