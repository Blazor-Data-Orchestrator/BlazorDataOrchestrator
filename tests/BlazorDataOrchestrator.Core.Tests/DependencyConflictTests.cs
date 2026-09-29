using System.Xml.Linq;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Testing;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class DependencyConflictTests
{
    [Theory(DisplayName = "O7: Core and the designer template do not reference AppHost-only Aspire.Hosting packages")]
    [Trait("Category", "Contract")]
    [InlineData("BlazorDataOrchestrator.Core", "BlazorDataOrchestrator.Core.csproj")]
    [InlineData("BlazorDataOrchestrator.JobCreatorTemplate", "BlazorDataOrchestrator.JobCreatorTemplate.csproj")]
    public void CoreAndTemplate_DoNotReferenceAspireHosting(string folder, string project)
    {
        var references = XDocument.Load(RepoPaths.Src(folder, project))
            .Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty);

        Assert.DoesNotContain(references, id => id.StartsWith("Aspire.Hosting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "O7: a job dependency older than a transitive requirement is explained")]
    [Trait("Category", "Contract")]
    public void FindDowngrades_NamesConstrainingPackage()
    {
        const string assets = """
            {
              "targets": {
                "net10.0": {
                  "Aspire.Hosting/13.5.4": { "type": "package", "dependencies": { "Humanizer.Core": "3.0.10", "Other": "[1.0.0, )" } },
                  "Microsoft.CodeAnalysis.Workspaces.Common/5.9.0": { "type": "package", "dependencies": { "Humanizer.Core": "2.14.1" } }
                }
              }
            }
            """;

        var warnings = DependencyConflictChecker.FindDowngrades(assets, new[]
        {
            new PackageDependency { Id = "Humanizer.Core", Version = "2.14.1" },
            new PackageDependency { Id = "Other", Version = "1.0" }
        });

        var warning = Assert.Single(warnings);
        Assert.Contains("Humanizer.Core 2.14.1", warning);
        Assert.Contains("3.0.10", warning);
        Assert.Contains("Aspire.Hosting 13.5.4", warning);
        Assert.Contains("NU1605", warning);
    }
}
