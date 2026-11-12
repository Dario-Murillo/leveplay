using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Leveplay.Core.Engine;

public enum EngineStart
{
    /// <summary>Leveplay launched the engine and will stop it on exit.</summary>
    Started,

    /// <summary>An engine was already running (started by the user); it is left alone.</summary>
    AlreadyRunning,
}

public sealed class EngineException(string message) : Exception(message);

/// <summary>
/// Runs Spotify Soloist, the headless Spotify Connect client that plays the audio, as a
/// hidden child process tied to the lifetime of the tray app.
/// </summary>
/// <param name="executablePath">Full path to <c>soloist.exe</c>.</param>
/// <param name="deviceName">Name the engine announces as a Spotify Connect device.</param>
/// <param name="log">Sink for the engine's own output. Never receives the API key.</param>
public sealed partial class SoloistEngine(string executablePath, string deviceName, Action<string>? log = null)
    : IDisposable
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(\.\d+)?: ")]
    private static partial Regex TimestampPrefix();

    /// <summary>Exit code Soloist uses when its build is older than 90 days.</summary>
    public const int ExpiredBuildExitCode = 10;

    private Process? _process;
    private bool _stopping;

    /// <summary>Raised with the exit code when the engine stops without being asked to.</summary>
    public event Action<int>? ExitedUnexpectedly;

    /// <summary>True while an engine launched by this instance is alive.</summary>
    public bool IsRunning => _process is { HasExited: false };

    public EngineStart Start(string apiKey)
    {
        if (IsRunning)
        {
            return EngineStart.Started;
        }

        // Two engines with the same device name would fight over the Connect session.
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)).Length > 0)
        {
            log?.Invoke("engine: already running, not starting another one");
            return EngineStart.AlreadyRunning;
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? "",
        };
        foreach (var argument in BuildArguments(deviceName, apiKey))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => LogLine(e.Data);
        process.ErrorDataReceived += (_, e) => LogLine(e.Data);
        process.Exited += (_, _) => OnExited(process);

        _stopping = false;
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new EngineException($"Could not start {executablePath}: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
        log?.Invoke($"engine: started pid {process.Id} as \"{deviceName}\"");
        return EngineStart.Started;
    }

    /// <summary>Stops the engine if this instance launched it. Safe to call repeatedly.</summary>
    public void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        _stopping = true;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
                log?.Invoke("engine: stopped");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose() => Stop();

    public static IReadOnlyList<string> BuildArguments(string deviceName, string apiKey) =>
        ["--device-name", deviceName, "--api-key", apiKey];

    /// <summary>Explains an exit code in terms the user can act on.</summary>
    public static string DescribeExit(int exitCode) => exitCode == ExpiredBuildExitCode
        ? "This Soloist build has expired (builds stop working after 90 days). Download the latest one."
        : $"Soloist stopped unexpectedly (exit code {exitCode}). See the log for details.";

    /// <summary>
    /// Keeps the log readable: Soloist prints a progress line every ten seconds while
    /// playing, which would drown everything else.
    /// </summary>
    public static bool IsWorthLogging(string line) =>
        !string.IsNullOrWhiteSpace(line) && !line.Contains(": playing spotify:", StringComparison.Ordinal);

    /// <summary>Drops Soloist's own timestamp; the log already adds one.</summary>
    public static string StripTimestamp(string line) => TimestampPrefix().Replace(line, "");

    private void LogLine(string? line)
    {
        if (line is not null && IsWorthLogging(line))
        {
            log?.Invoke($"engine: {StripTimestamp(line)}");
        }
    }

    private void OnExited(Process process)
    {
        if (_stopping)
        {
            return;
        }

        int exitCode;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        log?.Invoke($"engine: exited with code {exitCode}");
        ExitedUnexpectedly?.Invoke(exitCode);
    }
}
