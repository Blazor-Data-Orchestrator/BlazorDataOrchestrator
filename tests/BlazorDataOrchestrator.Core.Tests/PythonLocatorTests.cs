using BlazorDataOrchestrator.Core.Services;
using BlazorOrchestrator.Testing;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests;

public class PythonLocatorTests
{
    [Fact(DisplayName = "O8: a 0-byte WindowsApps alias is reported as the Store stub")]
    [Trait("Category", "Contract")]
    public void PythonLocator_RejectsZeroByteWindowsAppsAlias()
    {
        using var scope = new TestRunScope("o8-store-stub");
        var windowsApps = scope.CreateDirectory("WindowsApps");
        var alias = Path.Combine(windowsApps, "python.exe");
        File.WriteAllBytes(alias, Array.Empty<byte>());

        Assert.True(PythonLocator.IsWindowsAppsAlias(alias, windowsApps));
        Assert.False(PythonLocator.TryLocate(new[] { alias }, windowsApps, out var path, out var reason));
        Assert.Null(path);
        Assert.Equal(PythonLocatorFailure.WindowsStoreStubOnly, reason);
    }

    [Fact(DisplayName = "O8: a missing interpreter is reported as not installed")]
    [Trait("Category", "Contract")]
    public void PythonLocator_NothingFound_ReportsNotInstalled()
    {
        using var scope = new TestRunScope("o8-not-installed");

        Assert.False(PythonLocator.TryLocate(new[] { Path.Combine(scope.RootPath, "missing-python.exe") }, null, out _, out var reason));
        Assert.Equal(PythonLocatorFailure.NotInstalled, reason);
    }

    [Theory(DisplayName = "O8: only Python 3 version output is accepted")]
    [Trait("Category", "Contract")]
    [InlineData("Python 3.12.4", true)]
    [InlineData("Python 3.9.0\n", true)]
    [InlineData("Python 2.7.18", false)]
    [InlineData("Python was not found; run without arguments to install from the Microsoft Store, or disable this shortcut from Settings > Apps > Advanced app settings > App execution aliases.", false)]
    [InlineData("", false)]
    public void PythonLocator_RejectsNonPython3VersionOutput(string output, bool expected)
    {
        Assert.Equal(expected, PythonLocator.IsPython3VersionOutput(output));
    }
}
