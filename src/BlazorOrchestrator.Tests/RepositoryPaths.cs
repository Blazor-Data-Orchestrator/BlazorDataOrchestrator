namespace BlazorOrchestrator.Tests;

/// <summary>
/// Locates files in the repository from the test output folder.
/// </summary>
internal static class RepositoryPaths
{
    private const string SolutionFileName = "BlazorDataOrchestrator.slnx";

    public static string Root { get; } = FindRoot();

    public static string SqlVersionFile => Path.Combine(Root, "SQLVersion");

    public static string SqlScriptFolder => Path.Combine(Root, "src", "BlazorOrchestrator.Web", "!SQL");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
