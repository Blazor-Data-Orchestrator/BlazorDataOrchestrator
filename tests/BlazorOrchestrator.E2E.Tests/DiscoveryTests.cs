using Xunit;

namespace BlazorOrchestrator.E2E.Tests;

public class DiscoveryTests
{
    [Fact(DisplayName = "E2E test project is discoverable")]
    [Trait("Category", "Discovery")]
    public void E2ETestProjectIsDiscoverable() => Assert.True(true);
}