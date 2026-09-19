namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Raised when a job package cannot be built because its inputs are invalid.
/// The message is written straight to <see cref="NuGetPackageBuilderService.PackageBuildResult.ErrorMessage"/>,
/// so it must name the offending file or package and the required correction.
/// </summary>
public sealed class PackageBuildException : Exception
{
    public PackageBuildException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
