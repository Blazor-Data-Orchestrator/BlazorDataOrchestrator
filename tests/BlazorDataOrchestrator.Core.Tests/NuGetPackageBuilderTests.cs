using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Testing;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class NuGetPackageBuilderTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public async Task RejectsPackageIdThatCouldEscapeOutputDirectory()
    {
        using var scope = new TestRunScope("invalid-package-id");
        var result = await new NuGetPackageBuilderService().BuildPackageAsync(new NuGetPackageBuilderService.PackageBuildConfiguration
        {
            CodeRootPath = scope.RootPath,
            PackageId = "../unsafe"
        });

        Assert.False(result.Success);
        Assert.Contains("Invalid package id", result.ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task RejectsPackageWithoutLanguageEntryPoint()
    {
        using var scope = new TestRunScope("missing-entry-point");
        var result = await new NuGetPackageBuilderService().BuildPackageAsync(new NuGetPackageBuilderService.PackageBuildConfiguration
        {
            CodeRootPath = scope.RootPath,
            PackageId = "BDO.Tests.Empty"
        });

        Assert.False(result.Success);
        Assert.Contains("No code was packaged", result.ErrorMessage);
    }
}