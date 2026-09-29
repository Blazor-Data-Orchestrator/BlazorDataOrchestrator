using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>Why no usable Python 3 interpreter was found.</summary>
public enum PythonLocatorFailure
{
    None,
    NotInstalled,
    /// <summary>Only the Microsoft Store "App Execution Alias" placeholder answered.</summary>
    WindowsStoreStubOnly
}

/// <summary>
/// Finds a real Python 3 interpreter. Shared by the Agent and the designer.
/// </summary>
public static class PythonLocator
{
    private static readonly Regex Python3Version = new(@"^Python 3\.\d+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly object Gate = new();
    private static string? _cachedPath;

    public const string WindowsStoreStubGuidance =
        "Only the Microsoft Store placeholder was found. Install Python from python.org, " +
        "or turn off the 'App execution aliases' for python.exe in Windows Settings.";

    public static bool TryLocate(out string? path, out PythonLocatorFailure reason)
    {
        lock (Gate)
        {
            if (_cachedPath != null)
            {
                path = _cachedPath;
                reason = PythonLocatorFailure.None;
                return true;
            }
        }

        var windowsApps = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps")
            : null;

        if (!TryLocate(DefaultCandidates(), windowsApps, out path, out reason))
        {
            return false;
        }

        lock (Gate)
        {
            _cachedPath = path;
        }
        return true;
    }

    /// <summary>Uncached core of <see cref="TryLocate(out string?, out PythonLocatorFailure)"/>.</summary>
    internal static bool TryLocate(IEnumerable<string> candidates, string? windowsAppsDirectory,
        out string? path, out PythonLocatorFailure reason)
    {
        var sawStoreStub = false;

        foreach (var candidate in candidates)
        {
            var fullPath = ResolveOnPath(candidate);
            if (fullPath == null)
            {
                continue;
            }

            var isStoreAlias = IsWindowsAppsAlias(fullPath, windowsAppsDirectory);
            if (Probe(fullPath))
            {
                path = fullPath;
                reason = PythonLocatorFailure.None;
                return true;
            }

            sawStoreStub |= isStoreAlias;
        }

        path = null;
        reason = sawStoreStub ? PythonLocatorFailure.WindowsStoreStubOnly : PythonLocatorFailure.NotInstalled;
        return false;
    }

    /// <summary>True for a 0-byte App Execution Alias under the WindowsApps folder.</summary>
    internal static bool IsWindowsAppsAlias(string fullPath, string? windowsAppsDirectory)
    {
        if (string.IsNullOrEmpty(windowsAppsDirectory))
        {
            return false;
        }

        var directory = Path.GetFullPath(windowsAppsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(fullPath).StartsWith(directory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return new FileInfo(fullPath).Length == 0;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsPython3VersionOutput(string? output) =>
        !string.IsNullOrWhiteSpace(output) && Python3Version.IsMatch(output.Trim());

    internal static IReadOnlyList<string> DefaultCandidates()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new[]
            {
                "python",
                "python3",
                "py",
                @"C:\Python313\python.exe",
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Python39\python.exe",
                @"C:\Program Files\Python313\python.exe",
                @"C:\Program Files\Python312\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files\Python310\python.exe",
            };
        }

        // Linux / container: the Dockerfile symlink comes first.
        return new[]
        {
            "/usr/bin/python3",
            "/usr/local/bin/python3",
            "/usr/bin/python",
            "/usr/local/bin/python",
            "python3",
            "python",
        };
    }

    private static string? ResolveOnPath(string candidate)
    {
        if (Path.IsPathRooted(candidate))
        {
            return File.Exists(candidate) ? candidate : null;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var names = isWindows && !Path.HasExtension(candidate)
            ? new[] { candidate + ".exe", candidate }
            : new[] { candidate };

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var fullPath = Path.Combine(directory.Trim('"'), name);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    // Malformed PATH entry.
                }
            }
        }

        return null;
    }

    private static bool Probe(string fullPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fullPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            // Python 2 and some 3.x builds print the version to stderr.
            return process.ExitCode == 0 &&
                (IsPython3VersionOutput(stdout.GetAwaiter().GetResult()) ||
                 IsPython3VersionOutput(stderr.GetAwaiter().GetResult()));
        }
        catch
        {
            return false;
        }
    }
}
