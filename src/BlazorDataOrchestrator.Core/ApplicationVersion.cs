namespace BlazorDataOrchestrator.Core;

/// <summary>
/// Single source of truth for the application's code version.
/// This value drives the upgrade wizard (compared against the stored
/// <c>SchemaVersion</c> setting), the version displayed in the UI, and the
/// baseline version seeded on a fresh install.
///
/// Use the zero-padded <c>"NN.NN.NN"</c> format so it matches the SQL migration
/// script filenames in the <c>!SQL</c> folder (e.g. <c>01.10.00.sql</c>).
/// Bump this value whenever a new migration script is added.
/// </summary>
public static class ApplicationVersion
{
    /// <summary>
    /// The current application/code version, e.g. <c>"01.10.00"</c>.
    /// </summary>
    public const string Current = "01.10.00";
}
