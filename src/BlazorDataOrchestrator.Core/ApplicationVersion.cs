namespace BlazorDataOrchestrator.Core;

/// <summary>
/// Single source of truth for the application's code version.
/// This value drives the upgrade wizard (compared against the stored
/// <c>SchemaVersion</c> setting), the version displayed in the UI, the
/// baseline version seeded on a fresh install, and the release tag
/// (<c>01.60.00</c> is released as <c>v1.60.0</c>).
///
/// Use the zero-padded <c>"NN.NN.NN"</c> format so it matches the SQL migration
/// script filenames in the <c>!SQL</c> folder (e.g. <c>01.20.00.sql</c>).
/// Rules (enforced by VersionConsistencyTests and build/Check-Version.ps1):
/// <list type="bullet">
/// <item>The root <c>SQLVersion</c> file equals this value.</item>
/// <item>No <c>!SQL</c> script is newer than this value. A version bump does not need a
/// script (the Upgrade Wizard then only records the new version); a new script is named
/// after the version that introduces it, so bump this value with it.</item>
/// </list>
/// </summary>
public static class ApplicationVersion
{
    /// <summary>
    /// The current application/code version, e.g. <c>"01.20.00"</c>.
    /// </summary>
    public const string Current = "01.60.00";

    /// <summary>
    /// Converts a version such as <c>"01.20.00"</c> (or the older <c>"1.0"</c> format) to an
    /// integer for comparison: major * 10000 + minor * 100 + patch. Empty values are 0.
    /// </summary>
    public static int ToInteger(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 0;

        var result = 0;
        var segments = version.Trim().Split('.');
        var multipliers = new[] { 10000, 100, 1 };
        for (var i = 0; i < segments.Length && i < multipliers.Length; i++)
        {
            if (int.TryParse(segments[i], out var segment))
            {
                result += segment * multipliers[i];
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a release tag such as <c>"v1.60.0"</c> to the application version format
    /// <c>"01.60.00"</c>. Each part must be 0-99; pre-release suffixes are rejected.
    /// </summary>
    public static string FromReleaseTag(string tag)
    {
        var parts = (tag ?? string.Empty).Trim().TrimStart('v', 'V').Split('.');
        if (parts.Length != 3 || !parts.All(p => p.Length is > 0 and <= 2 && p.All(char.IsAsciiDigit)))
        {
            throw new FormatException($"'{tag}' is not a release tag in the form v<major>.<minor>.<patch> (each part 0-99).");
        }

        return string.Join(".", parts.Select(p => int.Parse(p).ToString("00")));
    }

    /// <summary>
    /// Converts an application version such as <c>"01.60.00"</c> to its release tag <c>"v1.60.0"</c>.
    /// </summary>
    public static string ToReleaseTag(string version)
    {
        var parts = (version ?? string.Empty).Trim().Split('.');
        if (parts.Length != 3 || !parts.All(p => p.Length == 2 && p.All(char.IsAsciiDigit)))
        {
            throw new FormatException($"'{version}' is not an application version in the form NN.NN.NN.");
        }

        return "v" + string.Join(".", parts.Select(p => int.Parse(p).ToString()));
    }
}
