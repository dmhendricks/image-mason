using System.Diagnostics;

namespace ImageMason.Imaging;

public sealed record ToolResult(int ExitCode, string StdOut, string StdErr, TimeSpan Elapsed);

/// <summary>A tool was killed from outside (SIGKILL), which in a container almost always means it ran out of memory.</summary>
public sealed class ToolKilledException(string tool) : Exception($"{tool} was killed, most likely because the container ran out of memory.")
{
    public string Tool { get; } = tool;
}

/// <summary>
/// Runs the CLI optimizers as child processes. Arguments go through ArgumentList, and the process is killed if the
/// request is cancelled or times out.
/// </summary>
public sealed class ToolRunner(ILogger<ToolRunner> logger)
{
    // Exit status 128 + 9: the process was ended by SIGKILL.
    private const int KilledExitCode = 137;

    // Raises the tool's oom_score_adj to the maximum before exec, so when the container runs out of memory the
    // kernel kills the tool, never the API process. Raising one's own score needs no privileges. "$0" is the tool.
    private const string OomFirstScript = "{ echo 1000 > /proc/self/oom_score_adj; } 2>/dev/null; exec \"$0\" \"$@\"";

    private static readonly bool CanAdjustOomScore = OperatingSystem.IsLinux() && File.Exists("/proc/self/oom_score_adj");

    /// <exception cref="ToolKilledException">The tool was killed from outside the service.</exception>
    public async Task<ToolResult> RunAsync(string tool, IEnumerable<string> args, CancellationToken ct)
    {
        var arguments = args.ToList();
        var psi = new ProcessStartInfo(CanAdjustOomScore ? "/bin/sh" : tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (CanAdjustOomScore)
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(OomFirstScript);
            psi.ArgumentList.Add(tool);
        }
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {tool}");

        var stdOut = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stdErr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited
            }
            throw;
        }

        var result = new ToolResult(process.ExitCode, await stdOut, await stdErr, stopwatch.Elapsed);

        logger.LogDebug("{Tool} {Args} -> exit {ExitCode} in {ElapsedMs} ms",
            tool, string.Join(' ', arguments), result.ExitCode, (int)result.Elapsed.TotalMilliseconds);

        // A cancelled run was killed by us and threw above, so a SIGKILL here came from outside.
        if (result.ExitCode == KilledExitCode)
            throw new ToolKilledException(tool);

        return result;
    }
}
