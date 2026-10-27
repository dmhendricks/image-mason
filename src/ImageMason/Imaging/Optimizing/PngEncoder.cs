using System.Globalization;
using ImageMason.Configuration;

namespace ImageMason.Imaging;

/// <summary>PNG: oxipng's lossless recompression, and pngquant's 256-color palette when the floor allows it.</summary>
internal sealed class PngEncoder(ToolRunner tools, ImageMasonOptions options, ILogger logger)
{
    // pngquant --quality caps, gentlest first
    private static readonly int[] PngQuantMaxQualities = [100, 95, 90, 80, 70, 55, 40];

    private readonly JobTools _tools = new(tools, logger);

    public async Task AddCandidatesAsync(OptimizeJob job, List<Candidate> candidates, CancellationToken ct)
    {
        // Lossless baseline: oxipng on the original, or on the resized pixels
        var lossless = await OxipngAsync(job.Input, Path.Combine(job.Work, "lossless.png"), alpha: false, ct);
        if (lossless is not null)
            candidates.Add(Candidate.FromFile(job.MethodName("oxipng"), lossless));
        else if (job.Input != job.Source)
            candidates.Add(Candidate.FromFile(job.MethodName("") is { Length: > 0 } changed ? changed.TrimEnd('+') : "decode", job.Input));

        if (job.MinScore is not { } minScore)
            return;

        var quantized = await QuantizeAsync(job, minScore, ct);
        if (quantized is null)
            return;

        // Full-strength recompression of the winner only; --alpha rewrites the RGB of fully transparent
        // pixels: invisible, but not pixel-identical.
        var optimized = await OxipngAsync(quantized.Path, Path.Combine(job.Work, "quantized-opt.png"), alpha: true, ct);
        var method = job.MethodName("pngquant+oxipng");
        candidates.Add(optimized is not null && new FileInfo(optimized).Length < quantized.Size
            ? Candidate.FromFile(method, optimized, quantized.Score)
            : quantized with { Method = method });
    }

    /// <summary>
    /// Reduces to a 256-color palette. Tries pngquant's gentlest cap first: if even that misses the floor
    /// (typical for photos, whose smooth gradients band at 256 colors), more aggressive caps can only score
    /// lower, so it stops. Otherwise it walks down the caps until one fails.
    /// Passing results are compared after a fast oxipng pass (-o 1): lower caps are not always smaller
    /// (heavier dithering compresses worse), and pngquant's raw size doesn't predict the recompressed size.
    /// No --skip-if-larger, for the same reason; the final candidate comparison guards against a larger result.
    /// </summary>
    private async Task<Candidate?> QuantizeAsync(OptimizeJob job, double minScore, CancellationToken ct)
    {
        Candidate? best = null;
        foreach (var maxQuality in PngQuantMaxQualities.Take(options.MaxPngQuantAttempts))
        {
            var output = Path.Combine(job.Work, $"quantized-{maxQuality}.png");
            if (!await _tools.RunForOutputAsync("pngquant",
                    ["--quality", $"0-{maxQuality}", "--speed", "1", "--force", "--output", output, "--", job.Input], output, ct))
                break;

            var score = await job.Scorer.ScoreAsync(output, ct);
            logger.LogDebug("pngquant 0-{MaxQuality}: {Bytes} bytes, score {Score:F2}", maxQuality, new FileInfo(output).Length, score);

            if (!(score >= minScore))
                break;

            var quick = await OxipngAsync(output, Path.Combine(job.Work, $"quantized-{maxQuality}-quick.png"), alpha: true, ct, level: "1");
            var candidate = Candidate.FromFile("pngquant", quick ?? output, score);

            if (best is null || candidate.Size < best.Size)
                best = candidate;
        }

        return best;
    }

    private async Task<string?> OxipngAsync(string input, string output, bool alpha, CancellationToken ct, string? level = null)
    {
        level ??= new FileInfo(input).Length > options.PngFastAboveBytes ? "2" : options.PngOptimizationLevel;
        List<string> args =
        [
            "--opt", level,
            "--strip", "safe",
            "--timeout", options.OxipngTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            "--out", output
        ];

        if (alpha)
            args.Add("--alpha");

        args.AddRange(["--", input]);

        return await _tools.RunForOutputAsync("oxipng", args, output, ct) ? output : null;
    }
}
