namespace ImageMason.Imaging;

/// <summary>Runs a job's tools and checks that each wrote its output, logging why when one didn't.</summary>
internal sealed class JobTools(ToolRunner tools, ILogger logger)
{
    /// <summary>True when the tool exited 0 and wrote <paramref name="output"/>; otherwise logs why.</summary>
    /// <param name="environment">Variables to set for this tool only; see <see cref="ToolRunner.RunAsync"/>.</param>
    /// <exception cref="ToolKilledException">The tool was killed from outside the service.</exception>
    public async Task<bool> RunForOutputAsync(string tool, IReadOnlyList<string> args, string output, CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = await tools.RunAsync(tool, args, ct, environment);
        if (result.ExitCode == 0 && File.Exists(output))
            return true;

        // vips's first argument is the operation, which says more than "vips".
        logger.LogWarning("{Tool} exited {ExitCode}: {StdErr}", tool == "vips" ? $"vips {args[0]}" : tool, result.ExitCode, result.StdErr);
        return false;
    }

    /// <summary>
    /// Runs a step the job can't do without, throwing <paramref name="message"/> to the caller when it fails. The
    /// message is safe to return; the tool's output is logged instead.
    /// </summary>
    /// <exception cref="ImageProcessingException">The tool failed or wrote no output.</exception>
    public async Task RequireOutputAsync(string tool, IReadOnlyList<string> args, string output, string message, CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!await RunForOutputAsync(tool, args, output, ct, environment))
            throw new ImageProcessingException(message);
    }
}
