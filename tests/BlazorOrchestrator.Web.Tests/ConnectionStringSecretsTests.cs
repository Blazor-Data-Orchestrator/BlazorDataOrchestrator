using BlazorOrchestrator.Web.Services;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>Review #6: no part of a secret may leak through the connection string description.</summary>
public class ConnectionStringSecretsTests
{
    [Theory(DisplayName = "Review #6: quoted, escaped and unquoted secrets never leak")]
    [Trait("Category", "Security")]
    // double-quoted password containing ';'
    [InlineData("Server=tcp:s.database.windows.net;Database=d;User Id=u;Password=\"alpha;secret-suffix\";Encrypt=true", "Server: tcp:s.database.windows.net; Database: d")]
    // single-quoted password containing ';'
    [InlineData("Server=s;Database=d;User Id=u;Password='alpha;secret-suffix';", "Server: s; Database: d")]
    // doubled (escaped) quotes inside the quoted value
    [InlineData("Server=s;Database=d;Password=\"al\"\"pha;secret-suffix\"", "Server: s; Database: d")]
    [InlineData("Server=s;Database=d;Pwd='it''s;secret-suffix'", "Server: s; Database: d")]
    // unquoted
    [InlineData("Data Source=s;Initial Catalog=d;User ID=u;Password=secret-suffix;TrustServerCertificate=true", "Server: s; Database: d")]
    // storage account keys and SAS
    [InlineData("DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=secret-suffix==;EndpointSuffix=core.windows.net", "Storage account: acct")]
    [InlineData("BlobEndpoint=https://acct.blob.core.windows.net/;SharedAccessSignature=sv=2024&sig=secret-suffix", "Storage endpoint: acct.blob.core.windows.net")]
    [InlineData("https://acct.blob.core.windows.net/?sv=2024&sig=secret-suffix", "https://acct.blob.core.windows.net")]
    [InlineData("https://user:secret-suffix@acct.table.core.windows.net/", "https://acct.table.core.windows.net")]
    [InlineData("UseDevelopmentStorage=true", "Local storage emulator (UseDevelopmentStorage=true)")]
    // unparseable or unknown: hidden, never echoed
    [InlineData("Password=\"unterminated;secret-suffix", ConnectionStringDisplay.Hidden)]
    [InlineData("SharedAccessKey=secret-suffix", ConnectionStringDisplay.Hidden)]
    public void Describe_ShowsOnlyNonSecretParts(string connectionString, string expected)
    {
        var described = ConnectionStringDisplay.Describe(connectionString);

        Assert.Equal(expected, described);
        Assert.DoesNotContain("secret", described, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void EnvironmentConnectionStrings_DetectsProvidedValuesAndContainerApps()
    {
        var variables = new Dictionary<string, string?>
        {
            ["ConnectionStrings__blazororchestratordb"] = "Server=tcp:x;Database=d",
            ["ConnectionStrings__queues"] = "   "
        };
        var environment = new EnvironmentConnectionStrings(name => variables.GetValueOrDefault(name));

        Assert.True(environment.IsProvided("blazororchestratordb"));
        Assert.False(environment.IsProvided("queues"));
        Assert.False(environment.IsRunningInContainerApps);
        Assert.False(environment.CanWriteAppSettings("blazororchestratordb"));
        Assert.True(environment.CanWriteAppSettings("blobs"));

        variables["CONTAINER_APP_NAME"] = "webapp";
        Assert.True(environment.IsRunningInContainerApps);
        Assert.False(environment.CanWriteAppSettings("blobs"));
    }
}
