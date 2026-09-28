namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// The assembly names that shipped with the host process. Built once from the Trusted Platform
/// Assemblies list, which is fixed at startup and never includes assemblies that jobs load later.
/// </summary>
internal static class HostAssemblyCatalog
{
    private static readonly Lazy<HashSet<string>> Names = new(() =>
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        return new HashSet<string>(
            tpa.Select(Path.GetFileNameWithoutExtension).Where(n => !string.IsNullOrEmpty(n))!,
            StringComparer.OrdinalIgnoreCase);
    });

    public static bool IsHostProvided(string simpleName) => Names.Value.Contains(simpleName);
}
