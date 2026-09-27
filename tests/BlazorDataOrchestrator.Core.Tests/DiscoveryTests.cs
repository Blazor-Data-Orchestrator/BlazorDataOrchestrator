using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class DiscoveryTests
{
    [Fact(DisplayName = "Core test project is discoverable")]
    [Trait("Category", "Discovery")]
    public void CoreTestProjectIsDiscoverable()
    {
        Assert.True(true);
    }
}