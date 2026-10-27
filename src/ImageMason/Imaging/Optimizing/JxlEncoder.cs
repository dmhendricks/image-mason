using System.Globalization;
using ImageMason.Configuration;

namespace ImageMason.Imaging;

/// <summary>
/// JPEG XL: cjxl's lossless recompression of a JPEG source, cjxl searched for the smallest passing distance, and
/// lossless from the pixels.
/// </summary>
internal sealed class JxlEncoder(ToolRunner tools, ImageMasonOptions options, ILogger logger)
{
    private readonly JobTools _tools = new(tools, logger);
    private readonly QualitySearch _search = new(options.MaxSearchSteps, logger);

    /// <param name="tryLossless">Also encode losslessly and keep it if smaller; graphics often compress better that way.</param>
    /// <param name="recompressJpeg">
    /// The source is a JPEG at its original size: cjxl can recompress its DCT data without loss, typically about 20%
    /// smaller with identical pixels.
    /// </param>
    public async Task AddCandidatesAsync(OptimizeJob job, List<Candidate> candidates, bool lossless, bool tryLossless,
        bool recompressJpeg, CancellationToken ct)
    {
        Candidate? recompressed = null;
        if (recompressJpeg)
        {
            // EXIF and XMP are dropped, as for the other formats; stripping them means the original JPEG file can't be
            // rebuilt byte for byte, but the pixels are the same. cjxl moves the EXIF orientation into the codestream.
            var output = Path.Combine(job.Work, "recompressed.jxl");
            if (await RunCjxlAsync(job.Source, output,
                    ["--lossless_jpeg=1", "-x", "strip=exif", "-x", "strip=xmp", "--allow_jpeg_reconstruction=0"], ct))
            {
                recompressed = Candidate.FromFile("cjxl-jpeg", output);
                candidates.Add(recompressed);
            }
        }

        Candidate? found = null;
        if (!lossless && job.MinScore is { } minScore)
        {
            var range = new SearchRange(
                Start: Math.Clamp(0.9 + (90 - minScore) * 0.1, options.MinJxlDistance, options.MaxJxlDistance),
                Min: options.MinJxlDistance, Max: options.MaxJxlDistance,
                Step: options.SearchStep, Precision: options.SearchPrecision, Smaller: 1, Integer: false);
            found = await _search.SearchAsync("cjxl", range, minScore, (d, suffix) => EncodeAsync(job, d, suffix, ct), ct);
            if (found is not null)
                candidates.Add(found);
        }

        // Lossless from the pixels: asked for, worth a try for graphics, or the fallback when nothing else worked.
        if (tryLossless || (found is null && recompressed is null))
        {
            var output = Path.Combine(job.Work, "lossless.jxl");
            if (await RunCjxlAsync(job.Input, output, ["-d", "0"], ct))
                candidates.Add(Candidate.FromFile(job.MethodName("cjxl-lossless"), output));
        }
    }

    private async Task<Candidate?> EncodeAsync(OptimizeJob job, double distance, string suffix, CancellationToken ct)
    {
        var d = distance.ToString("0.###", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"jxl-{suffix}.jxl");

        // cjxl recompresses a JPEG input losslessly unless told not to, whatever the distance; the reference is the
        // source JPEG itself when nothing else changes.
        if (!await RunCjxlAsync(job.Input, output, ["-d", d, "--lossless_jpeg=0"], ct))
            return null;

        // Scored from djxl's PNG, the pixels a viewer shows.
        var decoded = Path.Combine(job.Work, $"jxl-{suffix}.png");
        var score = await _tools.RunForOutputAsync("djxl", [output, decoded, "--quiet"], decoded, ct)
            ? await job.Scorer.ScoreAsync(decoded, ct)
            : null;
        return Candidate.FromFile(job.MethodName($"cjxl-d{d}"), output, score);
    }

    private Task<bool> RunCjxlAsync(string input, string output, string[] arguments, CancellationToken ct) =>
        _tools.RunForOutputAsync("cjxl",
            [input, output, .. arguments, "-e", options.JxlEffort.ToString(CultureInfo.InvariantCulture), "--quiet"], output, ct);
}
