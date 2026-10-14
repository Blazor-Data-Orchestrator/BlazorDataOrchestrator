using BlazorOrchestrator.Web.Services;

namespace BlazorOrchestrator.Tests;

public class EnvironmentConnectionStringsTests
{
    private static EnvironmentConnectionStrings Create(Dictionary<string, string> variables) =>
        new(name => variables.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void NothingSet_AllowsWritingAppSettings()
    {
        var environment = Create(new());

        Assert.Null(environment.GetProvided("blazororchestratordb"));
        Assert.False(environment.IsRunningInContainerApps);
        Assert.True(environment.CanWriteAppSettings("blazororchestratordb"));
    }

    [Fact]
    public void ProvidedConnectionString_IsNotWrittenToAppSettings()
    {
        var environment = Create(new() { ["ConnectionStrings__blazororchestratordb"] = "Server=tcp:x.database.windows.net;Database=db" });

        Assert.Equal("Server=tcp:x.database.windows.net;Database=db", environment.GetProvided("blazororchestratordb"));
        Assert.False(environment.CanWriteAppSettings("blazororchestratordb"));
        Assert.True(environment.CanWriteAppSettings("blobs"));
    }

    [Fact]
    public void ContainerApps_NeverWritesAppSettings()
    {
        var environment = Create(new() { ["CONTAINER_APP_NAME"] = "webapp" });

        Assert.True(environment.IsRunningInContainerApps);
        Assert.False(environment.CanWriteAppSettings("blobs", "tables", "queues"));
        Assert.Equal(EnvironmentConnectionStrings.ProvidedByAzureText, environment.ProvidedByText);
    }

    [Fact]
    public void BlankVariable_IsNotProvided()
    {
        var environment = Create(new() { ["ConnectionStrings__queues"] = "  " });

        Assert.False(environment.IsProvided("queues"));
    }

    [Theory]
    [InlineData("Server=s;Database=d;User Id=sa;Password=Secret!1;TrustServerCertificate=true", "Server=s;Database=d;User Id=sa;Password=********;TrustServerCertificate=true")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=a;AccountKey=abc==;EndpointSuffix=core.windows.net", "DefaultEndpointsProtocol=https;AccountName=a;AccountKey=********;EndpointSuffix=core.windows.net")]
    [InlineData("https://account.blob.core.windows.net/", "https://account.blob.core.windows.net/")]
    public void Mask_HidesSecrets(string connectionString, string expected)
    {
        Assert.Equal(expected, EnvironmentConnectionStrings.Mask(connectionString));
    }
}
