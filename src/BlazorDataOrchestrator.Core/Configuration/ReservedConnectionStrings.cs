namespace BlazorDataOrchestrator.Core.Configuration;

/// <summary>
/// The four infrastructure connection strings that are always owned by the executing host,
/// never by the job package.
/// </summary>
public sealed record ReservedConnectionStrings(
    string Blobs,
    string Queues,
    string Tables,
    string BlazorOrchestratorDb)
{
    public const string BlobsKey = "blobs";
    public const string QueuesKey = "queues";
    public const string TablesKey = "tables";
    public const string DbKey = "blazororchestratordb";

    public static readonly IReadOnlyList<string> ReservedKeys =
        new[] { BlobsKey, QueuesKey, TablesKey, DbKey };

    public static ReservedConnectionStrings Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty);

    public IDictionary<string, string> ToDictionary() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [BlobsKey] = Blobs,
        [QueuesKey] = Queues,
        [TablesKey] = Tables,
        [DbKey] = BlazorOrchestratorDb
    };

    public bool TryValidate(out IReadOnlyList<string> missing)
    {
        missing = ToDictionary()
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToArray();
        return missing.Count == 0;
    }

    public static bool IsReservedKey(string key) =>
        ReservedKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
}
