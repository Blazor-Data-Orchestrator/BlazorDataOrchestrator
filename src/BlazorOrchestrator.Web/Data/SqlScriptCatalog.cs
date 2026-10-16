using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BlazorDataOrchestrator.Core;

namespace BlazorOrchestrator.Web.Data;

/// <summary>
/// The versioned schema scripts in <c>!SQL</c> (<c>01.00.00.sql</c>, <c>01.20.00.sql</c>, ...).
/// <c>01.00.00.sql</c> is the full baseline a new database is created from; every newer script
/// upgrades an existing database and is applied only by the Upgrade Wizard.
/// </summary>
public static partial class SqlScriptCatalog
{
    public const string FolderName = "!SQL";
    public const string BaselineVersion = "01.00.00";
    public const string BaselineScript = BaselineVersion + ".sql";

    /// <summary>Assembly metadata key holding the script names the build found (see the .csproj).</summary>
    public const string ExpectedScriptsMetadataKey = "BlazorOrchestrator.SqlScripts";

    [GeneratedRegex(@"^\d{2}\.\d{2}\.\d{2}\.sql$", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptNamePattern();

    /// <summary>The !SQL folder copied next to the application.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, FolderName);

    /// <summary>
    /// The script names that were in !SQL when this assembly was built. A deployment that lost
    /// one of them must not be upgraded: the wizard would otherwise record the new version
    /// without running the missing script.
    /// </summary>
    public static IReadOnlyList<string> ExpectedScripts { get; } = ReadExpectedScripts();

    public static bool IsScriptName(string fileName) => ScriptNamePattern().IsMatch(fileName);

    public static string VersionOf(string scriptPath) => Path.GetFileNameWithoutExtension(scriptPath);

    /// <summary>Expected scripts that are not present in <paramref name="directory"/>.</summary>
    public static IReadOnlyList<string> FindMissing(string directory, IEnumerable<string> expectedScripts)
    {
        var present = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.sql").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string?>(StringComparer.OrdinalIgnoreCase);

        return expectedScripts
            .Where(name => !present.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Scripts newer than <paramref name="databaseVersion"/> and not newer than
    /// <paramref name="codeVersion"/>, in version order. Files that are not named NN.NN.NN.sql are ignored.
    /// </summary>
    public static IReadOnlyList<string> GetPendingScripts(IEnumerable<string> scriptPaths, string? databaseVersion, string codeVersion)
    {
        var databaseVersionInt = ApplicationVersion.ToInteger(databaseVersion);
        var codeVersionInt = ApplicationVersion.ToInteger(codeVersion);

        return scriptPaths
            .Where(path => IsScriptName(Path.GetFileName(path)))
            .Select(path => (Path: path, Version: ApplicationVersion.ToInteger(VersionOf(path))))
            .Where(script => script.Version > databaseVersionInt && script.Version <= codeVersionInt)
            .OrderBy(script => script.Version)
            .Select(script => script.Path)
            .ToList();
    }

    /// <summary>Splits a script into batches on lines that contain only <c>GO</c>.</summary>
    public static IEnumerable<string> SplitSqlBatches(string script)
    {
        var lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                yield return sb.ToString();
                sb.Clear();
            }
            else
            {
                sb.AppendLine(line);
            }
        }
        if (sb.Length > 0)
            yield return sb.ToString();
    }

    /// <summary>The non-empty batches of a script.</summary>
    public static IReadOnlyList<string> GetBatches(string script) =>
        SplitSqlBatches(script).Where(batch => !string.IsNullOrWhiteSpace(batch)).ToList();

    private static IReadOnlyList<string> ReadExpectedScripts()
    {
        var value = typeof(SqlScriptCatalog).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == ExpectedScriptsMetadataKey)?.Value;

        // Without the build metadata at least the baseline must be present.
        if (string.IsNullOrWhiteSpace(value))
        {
            return [BaselineScript];
        }

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
