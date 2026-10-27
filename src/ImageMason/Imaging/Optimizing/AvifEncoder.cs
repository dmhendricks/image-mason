using System.Globalization;
using ImageMason.Configuration;

namespace ImageMason.Imaging;

/// <summary>AVIF: avifenc searched for the smallest passing quality, with lossless as the fallback.</summary>
internal sealed class AvifEncoder(ToolRunner tools, ImageMasonOptions options, ILogger logger)
{
    private readonly JobTools _tools = new(tools, logger);
    private readonly QualitySearch _search = new(options.MaxSearchSteps, logger);

    public async Task AddCandidatesAsync(OptimizeJob job, List<Candidate> candidates, bool lossless, CancellationToken ct)
    {
        if (!lossless && job.MinScore is { } minScore)
        {
            var range = new SearchRange(
                Start: Math.Clamp(Math.Round(minScore + 2), options.MinAvifQuality, 100),
                Min: options.MinAvifQuality, Max: 100,
                Step: options.QualitySearchStep, Precision: 1, Smaller: -1, Integer: true);
            var found = await _search.SearchAsync("avifenc", range, minScore, (q, suffix) => EncodeAsync(job, q, suffix, ct), ct);
            if (found is not null)
            {
                candidates.Add(found);
                return;
            }
        }

        var output = Path.Combine(job.Work, "lossless.avif");
        if (await RunAvifencAsync(job, ["-l"], output, lossless: true, ct))
            candidates.Add(Candidate.FromFile(job.MethodName("avifenc-lossless"), output));
    }

    private async Task<Candidate?> EncodeAsync(OptimizeJob job, double quality, string suffix, CancellationToken ct)
    {
        var q = quality.ToString("0", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"avif-{suffix}.avif");
        if (!await RunAvifencAsync(job, ["-q", q], output, lossless: false, ct))
            return null;

        var decoded = Path.Combine(job.Work, $"avif-{suffix}.png");
        var score = await _tools.RunForOutputAsync("avifdec", ["--png-compress", "0", output, decoded], decoded, ct)
            ? await job.Scorer.ScoreAsync(decoded, ct)
            : null;
        return Candidate.FromFile(job.MethodName($"avifenc-q{q}"), output, score);
    }

    private Task<bool> RunAvifencAsync(OptimizeJob job, string[] quality, string output, bool lossless, CancellationToken ct)
    {
        List<string> args = ["-s", options.AvifSpeed.ToString(CultureInfo.InvariantCulture), "--ignore-exif", "--ignore-xmp", .. quality];

        // Without an ICC profile the pixels are sRGB: say so, or the decoded PNG has an "unspecified" transfer
        // function that some viewers can't interpret. Lossless needs the identity matrix (0).
        if (!job.Icc)
            args.AddRange(["--cicp", lossless ? "1/13/0" : "1/13/6"]);

        args.AddRange([job.Input, output]);

        return _tools.RunForOutputAsync("avifenc", args, output, ct);
    }
}
