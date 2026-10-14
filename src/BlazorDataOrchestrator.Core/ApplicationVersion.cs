namespace BlazorDataOrchestrator.Core
{
    /// <summary>
    /// The single source of truth for the application version.
    /// </summary>
    public static class ApplicationVersion
    {
        // Must match the newest !SQL script name, the root SQLVersion file and the release tag
        // (v1.0.0 -> 01.00.00). A unit test fails the build when they disagree.
        public const string Current = "01.00.00";

        /// <summary>
        /// Converts a version such as "01.00.00" (or the older "1.0" format) to an integer for
        /// comparison: major * 10000 + minor * 100 + patch. Empty or missing values are 0.
        /// </summary>
        public static int ToInteger(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return 0;

            int versionNumber = 0;
            var segments = version.Trim().Split('.');
            var multipliers = new[] { 10000, 100, 1 };

            for (int i = 0; i < segments.Length && i < multipliers.Length; i++)
            {
                if (int.TryParse(segments[i], out int segment))
                {
                    versionNumber += segment * multipliers[i];
                }
            }

            return versionNumber;
        }

        /// <summary>
        /// Converts a release tag such as "v1.0.0" to the application version format "01.00.00".
        /// </summary>
        public static string FromReleaseTag(string tag)
        {
            var parts = tag.Trim().TrimStart('v', 'V').Split('.');
            if (parts.Length != 3 || !parts.All(p => int.TryParse(p, out var n) && n is >= 0 and < 100))
            {
                throw new FormatException($"'{tag}' is not a release tag in the form v<major>.<minor>.<patch>.");
            }

            return string.Join(".", parts.Select(p => int.Parse(p).ToString("00")));
        }
    }
}
