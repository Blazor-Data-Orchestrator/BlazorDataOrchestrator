using BlazorOrchestrator.Testing;
using Xunit;

namespace BlazorOrchestrator.E2E.Tests;

public class WebJobScenarioTests
{
    [Fact(DisplayName = "WCS-01 Create Web Job C# without AI using a simple module")]
    [Trait("ScenarioId", "WCS-01"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WCS_01() => RunAsync("WCS-01");

    [Fact(DisplayName = "WCS-02 Create Web Job C# without AI using a dependency module")]
    [Trait("ScenarioId", "WCS-02"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WCS_02() => RunAsync("WCS-02");

    [Fact(DisplayName = "WCS-03 Create Web Job C# with AI using a simple module")]
    [Trait("ScenarioId", "WCS-03"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WCS_03() => RunAsync("WCS-03");

    [Fact(DisplayName = "WCS-04 Create Web Job C# with AI using a dependency module")]
    [Trait("ScenarioId", "WCS-04"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WCS_04() => RunAsync("WCS-04");

    [Fact(DisplayName = "WCS-05 Update Web Job C# without AI using a simple module")]
    [Trait("ScenarioId", "WCS-05"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WCS_05() => RunAsync("WCS-05");

    [Fact(DisplayName = "WCS-06 Update Web Job C# without AI using a dependency module")]
    [Trait("ScenarioId", "WCS-06"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WCS_06() => RunAsync("WCS-06");

    [Fact(DisplayName = "WCS-07 Update Web Job C# with AI using a simple module")]
    [Trait("ScenarioId", "WCS-07"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WCS_07() => RunAsync("WCS-07");

    [Fact(DisplayName = "WCS-08 Update Web Job C# with AI using a dependency module")]
    [Trait("ScenarioId", "WCS-08"), Trait("Surface", "WebJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WCS_08() => RunAsync("WCS-08");

    [Fact(DisplayName = "WPY-01 Create Web Job Python without AI using a simple module")]
    [Trait("ScenarioId", "WPY-01"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WPY_01() => RunAsync("WPY-01");

    [Fact(DisplayName = "WPY-02 Create Web Job Python without AI using a dependency module")]
    [Trait("ScenarioId", "WPY-02"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WPY_02() => RunAsync("WPY-02");

    [Fact(DisplayName = "WPY-03 Create Web Job Python with AI using a simple module")]
    [Trait("ScenarioId", "WPY-03"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WPY_03() => RunAsync("WPY-03");

    [Fact(DisplayName = "WPY-04 Create Web Job Python with AI using a dependency module")]
    [Trait("ScenarioId", "WPY-04"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WPY_04() => RunAsync("WPY-04");

    [Fact(DisplayName = "WPY-05 Update Web Job Python without AI using a simple module")]
    [Trait("ScenarioId", "WPY-05"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WPY_05() => RunAsync("WPY-05");

    [Fact(DisplayName = "WPY-06 Update Web Job Python without AI using a dependency module")]
    [Trait("ScenarioId", "WPY-06"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WPY_06() => RunAsync("WPY-06");

    [Fact(DisplayName = "WPY-07 Update Web Job Python with AI using a simple module")]
    [Trait("ScenarioId", "WPY-07"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task WPY_07() => RunAsync("WPY-07");

    [Fact(DisplayName = "WPY-08 Update Web Job Python with AI using a dependency module")]
    [Trait("ScenarioId", "WPY-08"), Trait("Surface", "WebJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task WPY_08() => RunAsync("WPY-08");

    private static async Task RunAsync(string id)
    {
        var result = await new JobScenarioRunner().RunAsync(JobScenarioCatalog.Get(id));
        Assert.StartsWith(id, result.RunId);
    }
}

public class VisualStudioJobScenarioTests
{
    [Fact(DisplayName = "VCS-01 Create Visual Studio Job C# without AI using a simple module")]
    [Trait("ScenarioId", "VCS-01"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VCS_01() => RunAsync("VCS-01");

    [Fact(DisplayName = "VCS-02 Create Visual Studio Job C# without AI using a dependency module")]
    [Trait("ScenarioId", "VCS-02"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VCS_02() => RunAsync("VCS-02");

    [Fact(DisplayName = "VCS-03 Create Visual Studio Job C# with AI using a simple module")]
    [Trait("ScenarioId", "VCS-03"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VCS_03() => RunAsync("VCS-03");

    [Fact(DisplayName = "VCS-04 Create Visual Studio Job C# with AI using a dependency module")]
    [Trait("ScenarioId", "VCS-04"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VCS_04() => RunAsync("VCS-04");

    [Fact(DisplayName = "VCS-05 Update Visual Studio Job C# without AI using a simple module")]
    [Trait("ScenarioId", "VCS-05"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VCS_05() => RunAsync("VCS-05");

    [Fact(DisplayName = "VCS-06 Update Visual Studio Job C# without AI using a dependency module")]
    [Trait("ScenarioId", "VCS-06"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VCS_06() => RunAsync("VCS-06");

    [Fact(DisplayName = "VCS-07 Update Visual Studio Job C# with AI using a simple module")]
    [Trait("ScenarioId", "VCS-07"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VCS_07() => RunAsync("VCS-07");

    [Fact(DisplayName = "VCS-08 Update Visual Studio Job C# with AI using a dependency module")]
    [Trait("ScenarioId", "VCS-08"), Trait("Surface", "VisualStudioJob"), Trait("Language", "CSharp"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VCS_08() => RunAsync("VCS-08");

    [Fact(DisplayName = "VPY-01 Create Visual Studio Job Python without AI using a simple module")]
    [Trait("ScenarioId", "VPY-01"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VPY_01() => RunAsync("VPY-01");

    [Fact(DisplayName = "VPY-02 Create Visual Studio Job Python without AI using a dependency module")]
    [Trait("ScenarioId", "VPY-02"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VPY_02() => RunAsync("VPY-02");

    [Fact(DisplayName = "VPY-03 Create Visual Studio Job Python with AI using a simple module")]
    [Trait("ScenarioId", "VPY-03"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VPY_03() => RunAsync("VPY-03");

    [Fact(DisplayName = "VPY-04 Create Visual Studio Job Python with AI using a dependency module")]
    [Trait("ScenarioId", "VPY-04"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Create"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VPY_04() => RunAsync("VPY-04");

    [Fact(DisplayName = "VPY-05 Update Visual Studio Job Python without AI using a simple module")]
    [Trait("ScenarioId", "VPY-05"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VPY_05() => RunAsync("VPY-05");

    [Fact(DisplayName = "VPY-06 Update Visual Studio Job Python without AI using a dependency module")]
    [Trait("ScenarioId", "VPY-06"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithoutAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VPY_06() => RunAsync("VPY-06");

    [Fact(DisplayName = "VPY-07 Update Visual Studio Job Python with AI using a simple module")]
    [Trait("ScenarioId", "VPY-07"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Simple"), Trait("Category", "Matrix")]
    public Task VPY_07() => RunAsync("VPY-07");

    [Fact(DisplayName = "VPY-08 Update Visual Studio Job Python with AI using a dependency module")]
    [Trait("ScenarioId", "VPY-08"), Trait("Surface", "VisualStudioJob"), Trait("Language", "Python"), Trait("Operation", "Update"), Trait("Authoring", "WithAI"), Trait("Module", "Dependency"), Trait("Category", "Matrix")]
    public Task VPY_08() => RunAsync("VPY-08");

    private static async Task RunAsync(string id)
    {
        var result = await new JobScenarioRunner().RunAsync(JobScenarioCatalog.Get(id));
        Assert.StartsWith(id, result.RunId);
    }
}