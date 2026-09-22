using Xunit;

namespace BlazorOrchestrator.Web.Tests;

public class DiscoveryTests
{
    [Fact(DisplayName = "Web test project is discoverable")]
    [Trait("Category", "Discovery")]
    public void WebTestProjectIsDiscoverable() => Assert.True(true);
}