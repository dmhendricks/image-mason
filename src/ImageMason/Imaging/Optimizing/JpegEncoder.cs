using System.Globalization;
using ImageMason.Configuration;

namespace ImageMason.Imaging;

/// <summary>JPEG: jpegtran's lossless recompression of a JPEG source, and jpegli searched for the smallest passing result.</summary>
internal sealed class JpegEncoder(ToolRunner tools, ImageMasonOptions options, ILogger logger)
{
    private readonly JobTools _tools = new(tools, logger);
    private readonly QualitySearch _search = new(options.MaxSearchSteps, logger);

    /// <param name="keepSource">The source is a JPEG at its original size: jpegtran can recompress it without loss.</param>
    public async Task AddCandidatesAsync(OptimizeJob job, List<Candidate> candidates, bool keepSource, CancellationToken ct)
    {
        if (keepSource)
        {
            // Stripping EXIF drops the orientation tag, so rotated phone photos would display sideways.
            // Keep all metadata when the image relies on it; otherwise keep only the ICC profile.
            var copy = job.Info.Orientation == 1 ? "icc" : "all";
            var lossless = Path.Combine(job.Work, "lossless.jpg");
            if (await _tools.RunForOutputAsync("jpegtran",
                    ["-copy", copy, "-optimize", "-progressive", "-outfile", lossless, job.Source], lossless, ct))
                candidates.Add(Candidate.FromFile("jpegtran", lossless));
        }

        if (job.MinScore is not { } minScore)
            return;

        var range = new SearchRange(
            Start: Math.Clamp(0.9 + (90 - minScore) * 0.1, options.MinJpegDistance, options.MaxJpegDistance),
            Min: options.MinJpegDistance, Max: options.MaxJpegDistance,
            Step: options.SearchStep, Precision: options.SearchPrecision, Smaller: 1, Integer: false);

        // A search where nothing passes always ends at the gentlest distance; that attempt is the fallback below.
        Candidate? gentlest = null;
        var found = await _search.SearchAsync("jpegli", range, minScore, async (d, suffix) =>
        {
            var attempt = await EncodeAsync(job, d, suffix, ct);
            if (d == options.MinJpegDistance)
                gentlest = attempt;
            return attempt;
        }, ct);
        if (found is not null)
        {
            candidates.Add(found);
            return;
        }

        // Without a lossless candidate (resized or converted), keep the gentlest encode even though it missed the floor.
        if (!keepSource)
        {
            var fallback = gentlest ?? await EncodeAsync(job, options.MinJpegDistance, "fallback", ct);
            if (fallback is not null)
                candidates.Add(fallback);
        }
    }

    private async Task<Candidate?> EncodeAsync(OptimizeJob job, double distance, string suffix, CancellationToken ct)
    {
        var d = distance.ToString("0.###", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"jpegli-{suffix}.jpg");
        if (!await _tools.RunForOutputAsync("cjpegli", [job.Input, output, $"--distance={d}"], output, ct))
            return null;

        return Candidate.FromFile(job.MethodName($"jpegli-d{d}"), output, await job.Scorer.ScoreAsync(output, ct));
    }
}
