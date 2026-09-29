using System.Text.Json;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Explains, before restore does, when a job dependency is older than a version another package in the
/// designer project requires. Turns a cryptic NU1605 (package downgrade) into a message naming the constraint.
/// </summary>
public static class DependencyConflictChecker
{
    public static List<string> FindDowngrades(string projectAssetsJson, IEnumerable<PackageDependency> dependencies)
    {
        var warnings = new List<string>();
        var wanted = dependencies
            .Where(d => TryParseMinimum(d.Version, out _))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return warnings;
        }

        using var document = JsonDocument.Parse(projectAssetsJson);
        if (!document.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
        {
            return warnings;
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("dependencies", out var libraryDependencies) ||
                    libraryDependencies.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var required in libraryDependencies.EnumerateObject())
                {
                    if (!wanted.TryGetValue(required.Name, out var job) ||
                        !TryParseMinimum(required.Value.GetString(), out var requiredMinimum) ||
                        !TryParseMinimum(job.Version, out var jobVersion) ||
                        jobVersion >= requiredMinimum)
                    {
                        continue;
                    }

                    var constrainingPackage = library.Name.Replace('/', ' ');
                    var minimumText = LowerBound(required.Value.GetString()!);
                    if (reported.Add($"{job.Id}|{constrainingPackage}"))
                    {
                        warnings.Add(
                            $"Job dependency {job.Id} {job.Version} is lower than {minimumText} required by " +
                            $"{constrainingPackage}. Restore will fail with NU1605 (package downgrade). " +
                            $"Use {job.Id} {minimumText} or later.");
                    }
                }
            }
        }

        return warnings;
    }

    /// <summary>Reads the lower bound of a NuGet version or range such as "1.2.3", "[1.2.3, )" or "1.2.3-beta".</summary>
    internal static bool TryParseMinimum(string? versionOrRange, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(versionOrRange))
        {
            return false;
        }

        var core = LowerBound(versionOrRange).Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        // System.Version orders 3.0 before 3.0.0; NuGet treats them as equal.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }

    private static string LowerBound(string versionOrRange) =>
        versionOrRange.Trim().TrimStart('[', '(').Split(',')[0].Trim().TrimEnd(']', ')');
}
