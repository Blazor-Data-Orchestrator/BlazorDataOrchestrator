using Microsoft.AspNetCore.Mvc.Testing;
using BlazorOrchistrator.Web;

namespace BlazorOrchistrator.Tests;

public class WebApplicationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebApplicationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Homepage_ReturnsSuccessAndCorrectContentType()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode(); // Status Code 200-299
        Assert.Equal("text/html; charset=utf-8",
            response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Get_Counter_Page_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/counter");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_Weather_Page_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/weather");

        // Assert
        response.EnsureSuccessStatusCode();
    }
}