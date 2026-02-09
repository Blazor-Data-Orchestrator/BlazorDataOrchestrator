using System.Diagnostics;
using GitHub.Copilot.SDK;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services;

/// <summary>
/// Lightweight singleton service that tracks Copilot CLI availability and connection health.
/// </summary>
public class CopilotHealthService
{
    private readonly ILogger<CopilotHealthService> _logger;
    private readonly CopilotClient _client;

    private bool _cliInstalled;
    private bool _startSucceeded;
    private string _statusMessage = "Checking Copilot CLI status…";
    private string? _errorDetail;

    public CopilotHealthService(CopilotClient client, ILogger<CopilotHealthService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// True when the CLI is installed, authenticated, and the CopilotClient is connected.
    /// </summary>
    public bool IsReady => _cliInstalled && _startSucceeded && _client.State == ConnectionState.Connected;

    /// <summary>
    /// Human-readable status string for the UI.
    /// </summary>
    public string StatusMessage => _statusMessage;

    /// <summary>
    /// Optional error detail from the last health check.
    /// </summary>
    public string? ErrorDetail => _errorDetail;

    /// <summary>
    /// Whether the Copilot CLI binary was found on the machine.
    /// </summary>
    public bool CliInstalled => _cliInstalled;

    /// <summary>
    /// Step-by-step install / auth instructions rendered in the UI.
    /// </summary>
    public static string InstallInstructions =>
        """
        The AI Chat feature requires the GitHub Copilot CLI.

        **To install & authenticate:**
        1. Download from [docs.github.com/en/copilot/how-tos/copilot-cli/install-copilot-cli](https://docs.github.com/en/copilot/how-tos/copilot-cli/install-copilot-cli) or run `winget install GitHub.Copilot`
        2. Run `copilot --version` to verify the install
        3. Authenticate using **one** of these methods:
           - Run `copilot login`
           - Set the `GITHUB_TOKEN` environment variable
           - Run `gh auth login` (if GitHub CLI is installed)
        4. Restart this application

        **Firewall:** Ensure outbound HTTPS to `api.githubcopilot.com` is allowed.
        """;

    /// <summary>
    /// Checks whether the Copilot CLI binary is available on the PATH.
    /// </summary>
    public async Task<bool> CheckCliInstalledAsync()
    {
        try
        {
            // First, check if the executable exists on PATH to avoid Win32Exception
            if (!await IsExecutableOnPathAsync("copilot"))
            {
                _cliInstalled = false;
                _logger.LogWarning("Copilot CLI not found on PATH");
                return _cliInstalled;
            }

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "copilot",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            _cliInstalled = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);

            if (_cliInstalled)
            {
                _logger.LogInformation("Copilot CLI detected: {Version}", output.Trim());
            }
            else
            {
                _logger.LogWarning("Copilot CLI returned exit code {ExitCode}", process.ExitCode);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _cliInstalled = false;
            _logger.LogWarning("Copilot CLI executable not found on PATH");
        }
        catch (Exception ex)
        {
            _cliInstalled = false;
            _logger.LogWarning(ex, "Copilot CLI not found on PATH");
        }

        return _cliInstalled;
    }

    /// <summary>
    /// Checks whether an executable exists on the system PATH without launching it directly.
    /// </summary>
    private static async Task<bool> IsExecutableOnPathAsync(string executableName)
    {
        try
        {
            using var process = new Process();

            if (OperatingSystem.IsWindows())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c where {executableName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"command -v {executableName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            process.Start();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether the Copilot CLI is authenticated by looking for the
    /// GITHUB_TOKEN environment variable or by running <c>copilot auth status</c>.
    /// </summary>
    public async Task<bool> CheckAuthenticatedAsync()
    {
        // 1. Quick check: GITHUB_TOKEN env var
        var token = System.Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _logger.LogInformation("Copilot authentication: GITHUB_TOKEN environment variable is set");
            return true;
        }

        // 2. Try `copilot auth status`
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "copilot",
                Arguments = "auth status",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!await IsExecutableOnPathAsync("copilot"))
            {
                _logger.LogDebug("Copilot CLI not found on PATH, skipping auth status check");
            }
            else
            {
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation("Copilot CLI authenticated: {Output}", output.Trim());
                    return true;
                }
                else
                {
                    _logger.LogWarning("Copilot CLI auth status returned exit code {ExitCode}", process.ExitCode);
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug("Could not run 'copilot auth status' — executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not run 'copilot auth status'");
        }

        // 3. Fallback: try `gh auth status`
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth status",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("GitHub CLI authenticated (gh auth status): {Output}", output.Trim());
                return true;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug("Could not run 'gh auth status' — executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not run 'gh auth status'");
        }

        _logger.LogWarning("Copilot CLI does not appear to be authenticated. " +
            "Set GITHUB_TOKEN, run 'copilot login', or run 'gh auth login'.");
        return false;
    }

    /// <summary>
    /// Sets the status after a <see cref="CopilotClient.StartAsync"/> attempt.
    /// </summary>
    public void SetStatus(string message, string? errorDetail = null)
    {
        _statusMessage = message;
        _errorDetail = errorDetail;

        if (errorDetail != null)
        {
            _startSucceeded = false;
            _logger.LogWarning("CopilotHealthService status: {Message} — {Detail}", message, errorDetail);
        }
        else
        {
            _startSucceeded = true;
            _logger.LogInformation("CopilotHealthService status: {Message}", message);
        }
    }

    /// <summary>
    /// Re-runs the health check (called from the UI's "Check Again" button).
    /// </summary>
    public async Task RefreshAsync()
    {
        await CheckCliInstalledAsync();

        if (!_cliInstalled)
        {
            SetStatus("Copilot CLI not found", "Install the Copilot CLI and restart your computer.");
            return;
        }

        // If CLI is installed, try to verify the client connection
        try
        {
            if (_client.State != ConnectionState.Connected)
            {
                await _client.StartAsync();
            }

            SetStatus("Connected");
        }
        catch (Exception ex)
        {
            SetStatus("Connection failed", ex.Message);
        }
    }
}
