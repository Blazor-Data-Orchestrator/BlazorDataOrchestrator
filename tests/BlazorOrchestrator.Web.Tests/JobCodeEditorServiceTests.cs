using BlazorOrchestrator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

public class JobCodeEditorServiceTests
{
    private static readonly string[] AllAppSettings =
    {
        "appsettings.json",
        "appsettings.Development.json",
        "appsettings.Staging.json",
        "appsettings.Production.json"
    };

    // GetFileListForLanguage touches none of the injected dependencies.
    private static JobCodeEditorService CreateService() =>
        new(null!, null!, NullLogger<JobCodeEditorService>.Instance, null!);

    [Theory(DisplayName = "O4: the Python editor lists all four appsettings files")]
    [Trait("Category", "Contract")]
    [InlineData("python")]
    [InlineData("py")]
    [InlineData("Python")]
    public void GetFileListForLanguage_Python_ContainsAllFourAppSettings(string language)
    {
        var files = CreateService().GetFileListForLanguage(language);

        Assert.Equal(new[] { "main.py", "requirements.txt" }.Concat(AllAppSettings), files);
    }

    [Theory(DisplayName = "O4: the C# editor lists all four appsettings files")]
    [Trait("Category", "Contract")]
    [InlineData("csharp")]
    [InlineData("cs")]
    [InlineData("anything-else")]
    public void GetFileListForLanguage_CSharp_ContainsAllFourAppSettings(string language)
    {
        var files = CreateService().GetFileListForLanguage(language);

        Assert.Equal(new[] { "main.cs" }.Concat(AllAppSettings).Append("BlazorDataOrchestrator.Job.nuspec"), files);
    }
}
