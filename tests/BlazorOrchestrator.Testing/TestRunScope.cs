using System.Text.RegularExpressions;

namespace BlazorOrchestrator.Testing;

public sealed class TestRunScope : IDisposable
{
    private static readonly Regex UnsafeCharacters = new("[^A-Za-z0-9_-]", RegexOptions.Compiled);

    public TestRunScope(string scenarioId)
    {
        var configuredRunId = Environment.GetEnvironmentVariable("BDO_TEST_RUN_ID");
        RunId = Sanitize(string.IsNullOrWhiteSpace(configuredRunId)
            ? $"{scenarioId}-{Guid.NewGuid():N}"
            : configuredRunId);
        RootPath = Path.Combine(Path.GetTempPath(), "bdo-tests", RunId, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RunId { get; }

    public string RootPath { get; }

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(RootPath, Sanitize(name));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }
        }
        catch
        {
        }
    }

    private static string Sanitize(string value)
    {
        var sanitized = UnsafeCharacters.Replace(value, "-");
        return sanitized.Length <= 100 ? sanitized : sanitized[..100];
    }
}