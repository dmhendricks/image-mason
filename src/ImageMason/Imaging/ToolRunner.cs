using System.Diagnostics;
using System.Text;

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
    internal const int KilledExitCode = 137;

    // Raises the tool's oom_score_adj to the maximum before exec, so when the container runs out of memory the
    // kernel kills the tool, never the API process. Raising one's own score needs no privileges. "$0" is the tool.
    private const string OomFirstScript = "{ echo 1000 > /proc/self/oom_score_adj; } 2>/dev/null; exec \"$0\" \"$@\"";

    private static readonly bool CanAdjustOomScore = OperatingSystem.IsLinux() && File.Exists("/proc/self/oom_score_adj");

    // Kept of each output stream; the rest is read and dropped so the tool never blocks on a full pipe. The tools
    // print a score, stats or error messages, far less than this.
    internal const int MaxOutputChars = 64 * 1024;

    // How long to wait for a killed tool to exit, so its files are closed before the job deletes its work folder.
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    /// <exception cref="ToolKilledException">The tool was killed from outside the service.</exception>
    public async Task<ToolResult> RunAsync(string tool, IEnumerable<string> args, CancellationToken ct)
    {
        var arguments = args.ToList();
        var stopwatch = Stopwatch.StartNew();
        using var process = Start(tool, arguments, standardInput: false);

        var stdOut = ReadCappedAsync(process.StandardOutput);
        var stdErr = ReadCappedAsync(process.StandardError);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            await KillAsync(process, tool);
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

    /// <summary>
    /// Starts a tool that stays running and answers each line written to it with one line, such as <c>ssim2
    /// reference -</c>. Dispose the session to end the tool.
    /// </summary>
    public ToolSession StartSession(string tool, IEnumerable<string> args) =>
        new(tool, Start(tool, args.ToList(), standardInput: true), this);

    private static Process Start(string tool, List<string> arguments, bool standardInput)
    {
        var psi = new ProcessStartInfo(CanAdjustOomScore ? "/bin/sh" : tool)
        {
            RedirectStandardInput = standardInput,
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

        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {tool}");
    }

    internal async Task KillAsync(Process process, string tool)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited
        }

        using var wait = new CancellationTokenSource(KillWait);
        try
        {
            await process.WaitForExitAsync(wait.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("{Tool} did not exit within {Seconds} s of being killed", tool, KillWait.TotalSeconds);
        }
    }

    internal static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var kept = new StringBuilder();
        var buffer = new char[8192];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer, CancellationToken.None)) > 0)
        {
            var keep = Math.Min(read, MaxOutputChars - kept.Length);
            kept.Append(buffer, 0, keep);
            truncated |= keep < read;
        }

        return truncated ? kept.Append(" [truncated]").ToString() : kept.ToString();
    }
}

/// <summary>A running tool that answers one line per request line. Requests must not overlap.</summary>
public sealed class ToolSession : IAsyncDisposable
{
    private readonly string _tool;
    private readonly Process _process;
    private readonly ToolRunner _runner;
    private readonly Task<string> _stdErr;

    internal ToolSession(string tool, Process process, ToolRunner runner)
    {
        _tool = tool;
        _process = process;
        _runner = runner;
        _stdErr = ToolRunner.ReadCappedAsync(process.StandardError);
    }

    /// <summary>What the tool wrote to stderr, once it has exited.</summary>
    public string StdErr => _stdErr.IsCompleted ? _stdErr.Result : "";

    /// <summary>Sends one line and returns the tool's reply, or null if the tool exited instead of replying.</summary>
    /// <exception cref="ToolKilledException">The tool was killed from outside the service.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the tool is killed and the session can't be used again.</exception>
    public async Task<string?> RequestAsync(string line, CancellationToken ct)
    {
        try
        {
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
            var reply = await _process.StandardOutput.ReadLineAsync(ct);
            if (reply is not null)
                return reply;
        }
        catch (OperationCanceledException)
        {
            await _runner.KillAsync(_process, _tool);
            throw;
        }
        catch (IOException)
        {
            // The tool exited and closed its stdin.
        }

        await _process.WaitForExitAsync(CancellationToken.None);
        await _stdErr;
        if (_process.ExitCode == ToolRunner.KilledExitCode)
            throw new ToolKilledException(_tool);
        return null;
    }

    /// <summary>Closes the tool's stdin, which ends it, and waits for it to exit; kills it if it doesn't.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _process.StandardInput.Close();
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(wait.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            await _runner.KillAsync(_process, _tool);
        }
        _process.Dispose();
    }
}
