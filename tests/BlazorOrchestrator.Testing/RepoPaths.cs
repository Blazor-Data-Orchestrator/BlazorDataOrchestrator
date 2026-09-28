namespace BlazorOrchestrator.Testing;

public static class RepoPaths
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlazorDataOrchestrator.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root (BlazorDataOrchestrator.slnx).");
    });

    public static string Root => RootPath.Value;

    public static string Src(params string[] parts) => Path.Combine(new[] { Root, "src" }.Concat(parts).ToArray());
}
