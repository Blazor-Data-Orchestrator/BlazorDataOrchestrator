namespace BlazorDataOrchestrator.Core.Configuration;

/// <summary>
/// Fixed local-development endpoints shared by the AppHost (linked source) and generated designer projects.
/// The Azurite ports are deliberately not 10000-10002, so a stray standalone Azurite cannot capture designer traffic.
/// </summary>
public static class LocalDevEndpoints
{
    public const string Host = "127.0.0.1";
    public const int BlobPort = 10100;
    public const int QueuePort = 10101;
    public const int TablePort = 10102;
    public const int SqlPort = 14330;

    // Public, well-known Azurite development credentials.
    public const string AccountName = "devstoreaccount1";
    public const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public static string BlobConnectionString => Build("BlobEndpoint", BlobPort);
    public static string QueueConnectionString => Build("QueueEndpoint", QueuePort);
    public static string TableConnectionString => Build("TableEndpoint", TablePort);

    private static string Build(string endpointKey, int port) =>
        $"DefaultEndpointsProtocol=http;AccountName={AccountName};AccountKey={AccountKey};{endpointKey}=http://{Host}:{port}/{AccountName};";
}
