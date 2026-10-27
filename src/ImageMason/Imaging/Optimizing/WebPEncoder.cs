using System.Globalization;
using ImageMason.Configuration;

namespace ImageMason.Imaging;

/// <summary>WebP: cwebp searched for the smallest passing quality, and lossless.</summary>
internal sealed class WebPEncoder(ToolRunner tools, ImageMasonOptions options, ILogger logger)
{
    private readonly JobTools _tools = new(tools, logger);
    private readonly QualitySearch _search = new(options.MaxSearchSteps, logger);

    /// <param name="tryLossless">Also encode losslessly and keep it if smaller; graphics often compress better that way.</param>
    public async Task AddCandidatesAsync(OptimizeJob job, List<Candidate> candidates, bool lossless, bool tryLossless, CancellationToken ct)
    {
        Candidate? found = null;
        if (!lossless && job.MinScore is { } minScore)
        {
            var range = new SearchRange(
                Start: Math.Clamp(Math.Round(minScore + 9), options.MinWebPQuality, 100),
                Min: options.MinWebPQuality, Max: 100,
                Step: options.QualitySearchStep, Precision: 1, Smaller: -1, Integer: true);
            found = await _search.SearchAsync("cwebp", range, minScore, (q, suffix) => EncodeAsync(job, q, suffix, ct), ct);
            if (found is not null)
                candidates.Add(found);
        }

        // Lossy WebP is 4:2:0 only, so some images never reach a high floor; lossless is the fallback.
        if (tryLossless || found is null)
        {
            var output = Path.Combine(job.Work, "lossless.webp");
            if (await _tools.RunForOutputAsync("cwebp",
                [
                    "-quiet", "-lossless", "-exact", "-z", options.WebPLosslessEffort.ToString(CultureInfo.InvariantCulture),
                    "-metadata", "icc", job.Input, "-o", output
                ], output, ct))
                candidates.Add(Candidate.FromFile(job.MethodName("cwebp-lossless"), output));
        }
    }

    private async Task<Candidate?> EncodeAsync(OptimizeJob job, double quality, string suffix, CancellationToken ct)
    {
        var q = quality.ToString("0", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"webp-{suffix}.webp");
        if (!await _tools.RunForOutputAsync("cwebp",
                ["-quiet", "-m", "4", "-sharp_yuv", "-q", q, "-metadata", "icc", job.Input, "-o", output], output, ct))
            return null;

        // The scorer can't read WebP; vips decodes it and keeps the ICC profile (dwebp drops it).
        var decoded = Path.Combine(job.Work, $"webp-{suffix}.png");
        var score = await _tools.RunForOutputAsync("vips", ["copy", output, $"{decoded}[keep=icc,compression=0]"], decoded, ct)
            ? await job.Scorer.ScoreAsync(decoded, ct)
            : null;
        return Candidate.FromFile(job.MethodName($"cwebp-q{q}"), output, score);
    }
}
