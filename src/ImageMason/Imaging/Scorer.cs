using System.Globalization;

namespace ImageMason.Imaging;

/// <summary>
/// SSIMULACRA2 scores of a job's candidates against its reference (100 = identical, 90+ = visually lossless), from one
/// ssim2 process that prepares the reference once and scores each candidate against it. Started on the first score,
/// so lossless jobs never start it; dispose before deleting the job's files.
/// </summary>
internal sealed class Scorer(ToolRunner tools, string reference, ILogger logger) : IAsyncDisposable
{
    private const string Tool = "ssim2";

    private ToolSession? _session;

    /// <summary>The score, or null when the candidate couldn't be scored (the reason is logged).</summary>
    /// <exception cref="ToolKilledException">The scorer was killed from outside, most likely out of memory.</exception>
    public async Task<double?> ScoreAsync(string candidate, CancellationToken ct)
    {
        _session ??= tools.StartSession(Tool, [reference, "-"]);
        var reply = await _session.RequestAsync(candidate, ct);
        if (double.TryParse(reply, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
            return score;

        if (reply is null)
        {
            // The scorer exited; the next candidate starts a new one.
            logger.LogWarning("{Tool} exited: {StdErr}", Tool, _session.StdErr);
            await _session.DisposeAsync();
            _session = null;
        }
        else
        {
            logger.LogWarning("{Tool} couldn't score {Candidate}: {Reply}", Tool, Path.GetFileName(candidate), reply);
        }
        return null;
    }

    public ValueTask DisposeAsync() => _session?.DisposeAsync() ?? ValueTask.CompletedTask;
}
