using System.Globalization;
using ImageMason.Configuration;

namespace ImageMason.Imaging;

public enum QualityPreset
{
    /// <summary>Pixels stay identical: oxipng for PNG, jpegtran for JPEG, lossless WebP, AVIF and JPEG XL.</summary>
    Lossless,

    /// <summary>Not noticeable in place at 1:1 (SSIMULACRA2 85+ by default).</summary>
    High,

    /// <summary>Not noticeable side by side at 1:1 (80+ by default).</summary>
    Medium,

    /// <summary>Barely noticeable side by side (70+ by default).</summary>
    Low
}

/// <param name="MinScore">Overrides the preset's minimum score. Ignored for lossless unless the result can't be lossless.</param>
/// <param name="MaxWidth">Overrides the default max width. 0 disables resizing. Ignored when <paramref name="Resize"/> is set.</param>
/// <param name="Format">Output format. Null keeps the input's format.</param>
/// <param name="Resize">Resize to a box; see <see cref="ResizeRequest"/>. Applies to the cropped area when <paramref name="Rect"/> is set.</param>
/// <param name="Rect">The area to keep, cut out before any resize.</param>
public sealed record OptimizeRequest(QualityPreset Quality, double? MinScore = null, int? MaxWidth = null, ImageFormat? Format = null,
    ResizeRequest? Resize = null, CropRect? Rect = null);

/// <param name="Score">SSIMULACRA2 vs. the original, or vs. the cropped or resized original when the image was changed.</param>
/// <param name="MinScore">The floor the result had to meet; null for lossless.</param>
public sealed record OptimizeResult(
    byte[] Data,
    ImageFormat Format,
    long OriginalSize,
    ImageInfo OriginalInfo,
    ImageInfo Info,
    string Method,
    double? Score,
    double? MinScore);

public sealed class UnsupportedImageException(string message) : Exception(message);

public sealed class ImageTooLargeException(string message) : Exception(message);

public sealed class CropOutsideImageException(string message) : Exception(message);

public sealed class ImageOptimizer(ToolRunner tools, JobMemoryGate memory, ImageMasonOptions options, ILogger<ImageOptimizer> logger)
{
    private const string Original = "original";

    // Peak container memory per pixel, measured on 12-48 megapixel images and rounded up (work files in /tmp count,
    // since tmpfs is memory). Scoring with SSIMULACRA2 dominates: lossy presets need about 10x a lossless pass.
    internal const long ScoringBytesPerPixel = 150;
    internal const long PngLosslessBytesPerPixel = 48;
    internal const long JpegLosslessBytesPerPixel = 16;
    internal const long WebPLosslessBytesPerPixel = 32;
    internal const long AvifLosslessBytesPerPixel = 40;

    // cjxl alone peaked at 23 bytes per pixel lossless and 33 lossy (effort 7, 12 MP), djxl at 8; a whole lossless
    // JPEG-to-JPEG XL job at 15.6 MP came to 22. Lossy encodes run between scoring passes, which dominate.
    internal const long JxlLosslessBytesPerPixel = 32;
    internal const long JobOverheadBytes = 64L * 1024 * 1024;

    // pngquant --quality caps, gentlest first
    private static readonly int[] PngQuantMaxQualities = [100, 95, 90, 80, 70, 55, 40];

    // Enough of a PNG to reach the first IDAT, where the iCCP and tRNS chunks end.
    private const int PngHeadBytes = 4 * 1024 * 1024;

    private sealed record Candidate(string Method, string Path, long Size, double? Score);

    /// <param name="Reference">The pixels results are encoded from and scored against: the source, or a normalized PNG.</param>
    /// <param name="Cropped">Cropped; method names start with "crop+".</param>
    /// <param name="Resized">Resized; method names start with "resize+" (after "crop+").</param>
    /// <param name="Icc">The reference embeds an ICC profile, which the encoders copy.</param>
    private sealed record Job(string Work, string Source, string Reference, ImageInfo Info, bool Cropped, bool Resized, double? MinScore, bool Icc);

    /// <summary>One encoder setting searched for the smallest result that meets the floor.</summary>
    /// <param name="Smaller">+1 when a higher value gives a smaller file (jpegli distance), -1 when lower does (quality).</param>
    /// <param name="Integer">Only whole values are valid.</param>
    private sealed record SearchRange(double Start, double Min, double Max, double Step, double Precision, int Smaller, bool Integer);

    public async Task<OptimizeResult> OptimizeAsync(byte[] input, OptimizeRequest request, CancellationToken ct)
    {
        var format = ImageFormatDetector.Detect(input);
        if (format == ImageFormat.Unknown)
            throw new UnsupportedImageException($"Unsupported image format; expected {ImageFormatDetector.SupportedNames}.");

        var info = ReadInfo(input, format, options.MaxPixels);
        if (info.Animated)
            throw new UnsupportedImageException("Animated images are not supported.");

        var target = request.Format ?? format;
        var converting = target != format;
        var lossless = request.Quality == QualityPreset.Lossless;
        var rect = request.Rect;
        if (rect is not null && !rect.FitsWithin(info.DisplayWidth, info.DisplayHeight))
            throw new CropOutsideImageException(string.Create(CultureInfo.InvariantCulture,
                $"rect {rect} reaches past the image, which is {info.DisplayWidth}x{info.DisplayHeight} as displayed."));

        // Resizing applies to the cropped area.
        var crop = rect is not null && !rect.Covers(info.DisplayWidth, info.DisplayHeight);
        var (sourceWidth, sourceHeight) = crop ? (rect!.Width, rect.Height) : (info.DisplayWidth, info.DisplayHeight);

        var maxWidth = request.MaxWidth ?? (lossless ? 0 : options.DefaultMaxWidth);
        var sizing = request.Resize ?? (maxWidth > 0 ? new ResizeRequest(maxWidth, null) : null);
        var (outputWidth, outputHeight) = sizing?.OutputSize(sourceWidth, sourceHeight) ?? (sourceWidth, sourceHeight);
        var resize = (outputWidth, outputHeight) != (sourceWidth, sourceHeight);
        CheckOutputSize(target, outputWidth, outputHeight);

        // Only a lossless JPEG-to-JPEG pass keeps the orientation tag; every re-encode starts from upright pixels.
        var rotate = info.Orientation != 1 && !(lossless && !converting && format == ImageFormat.Jpeg);

        double? minScore;
        if (!lossless)
        {
            minScore = request.MinScore ?? request.Quality switch
            {
                QualityPreset.High => options.HighMinScore,
                QualityPreset.Medium => options.MediumMinScore,
                _ => options.LowMinScore
            };
        }
        else if (target == ImageFormat.Jpeg && (crop || resize || converting))
        {
            // JPEG has no lossless mode: a cropped, resized or converted JPEG is re-encoded and held to a high floor.
            minScore = request.MinScore ?? options.NearLosslessMinScore;
        }
        else
        {
            minScore = null;
        }

        var scored = minScore is not null;
        var outputPixels = (long)outputWidth * outputHeight;
        using var reservation = await memory.ReserveAsync(EstimateMemory(format, target, info.PixelCount, outputPixels, scored), ct);

        var work = Directory.CreateTempSubdirectory("image-mason-");
        try
        {
            var source = Path.Combine(work.FullName, "source" + format.Extension());
            await File.WriteAllBytesAsync(source, input, ct);

            // The encoders and ssimulacra2 read PNG and JPEG; oxipng and pngquant read PNG only.
            var normalize = resize || rotate
                || format is ImageFormat.WebP or ImageFormat.Avif or ImageFormat.Jxl
                || (target == ImageFormat.Png && format != ImageFormat.Png);
            string reference;
            if (crop)
            {
                reference = await CropAsync(source, format, info, rect!, work.FullName, ct);
                if (resize)
                    reference = await NormalizeAsync(reference, ImageFormat.Png, work.FullName, sizing!.VipsArguments(), ct);
            }
            else
            {
                reference = normalize
                    ? await NormalizeAsync(source, format, work.FullName,
                        resize ? sizing!.VipsArguments() : ["--size", $"{info.DisplayWidth}x"], ct)
                    : source;
            }

            var referenceFormat = reference == source ? format : ImageFormat.Png;
            var head = reference == source ? input : await ReadHeadAsync(reference, ct);

            // JPEG has no alpha: flatten onto white, which is also what the result is scored against.
            if (target == ImageFormat.Jpeg && referenceFormat == ImageFormat.Png && ImageHeader.PngHasAlpha(head))
            {
                reference = await FlattenAsync(reference, work.FullName, ImageHeader.PngBitDepth(head), ct);
                head = await ReadHeadAsync(reference, ct);
            }

            var job = new Job(work.FullName, source, reference, info, crop, resize, minScore, ImageHeader.HasIccProfile(head, referenceFormat));

            // Size guard: unless the image must be cropped, resized or converted, the original is a candidate and wins ties.
            List<Candidate> candidates = crop || resize || converting ? [] : [new(Original, source, input.Length, null)];

            switch (target)
            {
                case ImageFormat.Jpeg:
                    await OptimizeJpegAsync(job, candidates, keepSource: format == ImageFormat.Jpeg && !crop && !resize, ct);
                    break;
                case ImageFormat.Png:
                    await OptimizePngAsync(job, candidates, ct);
                    break;
                case ImageFormat.WebP:
                    await OptimizeWebPAsync(job, candidates, lossless,
                        tryLossless: lossless || (format == ImageFormat.Png && !PngPhoto.IsPhoto(input)), ct);
                    break;
                case ImageFormat.Avif:
                    await OptimizeAvifAsync(job, candidates, lossless, ct);
                    break;
                case ImageFormat.Jxl:
                    await OptimizeJxlAsync(job, candidates, lossless,
                        tryLossless: format == ImageFormat.Png && !PngPhoto.IsPhoto(input),
                        recompressJpeg: format == ImageFormat.Jpeg && !crop && !resize, ct);
                    break;
                default:
                    throw new UnsupportedImageException($"Unsupported output format {target}.");
            }

            var best = candidates.MinBy(c => c.Size);
            if (best is null)
            {
                if (converting)
                    throw new InvalidOperationException($"Could not encode the {format.Name()} image as {target.Name()}.");

                // The original would ignore the requested area.
                if (crop)
                    throw new InvalidOperationException($"Could not encode the cropped {format.Name()} image.");

                logger.LogWarning("No resized result was produced; returning the original");
                best = new(Original, source, input.Length, null);
            }

            var data = best.Method == Original ? input : await File.ReadAllBytesAsync(best.Path, ct);

            return new OptimizeResult(data, target, input.Length, info, ImageInfo.Read(data, target), best.Method, best.Score, minScore);
        }
        finally
        {
            try
            {
                work.Delete(recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to delete work directory {Path}", work.FullName);
            }
        }
    }

    /// <summary>
    /// Estimated peak memory of one job: a lossless pass over the larger of the input and output, in the larger of
    /// the two formats, plus scoring at the output size when the result is lossy. Errs high by 10-35% against
    /// measurements.
    /// </summary>
    internal static long EstimateMemory(ImageFormat format, ImageFormat target, long inputPixels, long outputPixels, bool scored)
    {
        var lossless = Math.Max(inputPixels, outputPixels) * Math.Max(LosslessBytesPerPixel(format), LosslessBytesPerPixel(target));
        return JobOverheadBytes + lossless + (scored ? outputPixels * ScoringBytesPerPixel : 0);
    }

    private static long LosslessBytesPerPixel(ImageFormat format) => format switch
    {
        ImageFormat.Png => PngLosslessBytesPerPixel,
        ImageFormat.WebP => WebPLosslessBytesPerPixel,
        ImageFormat.Avif => AvifLosslessBytesPerPixel,
        ImageFormat.Jxl => JxlLosslessBytesPerPixel,
        _ => JpegLosslessBytesPerPixel
    };

    /// <summary>Largest width or height <paramref name="format"/> can store; null when only the pixel limit applies.</summary>
    public static int? MaxSide(ImageFormat format) => format switch
    {
        ImageFormat.WebP => 16383,
        ImageFormat.Jpeg => 65535,
        _ => null
    };

    /// <summary>Rejects results the pixel limit or the output format can't hold, before any work starts.</summary>
    private void CheckOutputSize(ImageFormat target, int width, int height)
    {
        var pixels = (long)width * height;
        if (pixels > options.MaxPixels)
            throw new ImageTooLargeException(string.Create(CultureInfo.InvariantCulture,
                $"The result would be {width}x{height} ({pixels / 1e6:0.#} megapixels); the limit is {options.MaxPixels / 1e6:0.#} megapixels."));

        var (name, maxSide) = target switch
        {
            ImageFormat.WebP => ("WebP", MaxSide(target)!.Value),
            ImageFormat.Jpeg => ("JPEG", MaxSide(target)!.Value),
            _ => ("", int.MaxValue)
        };
        if (width > maxSide || height > maxSide)
            throw new ImageTooLargeException(string.Create(CultureInfo.InvariantCulture,
                $"{name} images can be at most {maxSide} pixels on each side; the result would be {width}x{height}."));
    }

    /// <summary>
    /// Reads the dimensions from the header and enforces the pixel limit before any tool decodes the image.
    /// The decoders size their buffers from these same header values, so the check bounds their memory use.
    /// </summary>
    /// <exception cref="UnsupportedImageException">The dimensions can't be read.</exception>
    /// <exception cref="ImageTooLargeException">The image has more than <paramref name="maxPixels"/> pixels.</exception>
    public static ImageInfo ReadInfo(byte[] data, ImageFormat format, long maxPixels)
    {
        var info = ImageInfo.Read(data, format);
        if (info.Width <= 0 || info.Height <= 0)
            throw new UnsupportedImageException("Could not read the image dimensions.");

        if (info.PixelCount > maxPixels)
            throw new ImageTooLargeException(string.Create(CultureInfo.InvariantCulture,
                $"The image is {info.Width}x{info.Height} ({info.PixelCount / 1e6:0.#} megapixels); the limit is {maxPixels / 1e6:0.#} megapixels."));

        return info;
    }

    /// <summary>
    /// Writes the pixels the encoders start from as a lossless PNG, resized by <paramref name="sizeArguments"/> (a
    /// no-op size just bakes in the orientation), keeping only the ICC profile.
    /// AVIF is decoded by avifdec and JPEG XL by djxl first; vipsthumbnail reads the other formats. Work PNGs are
    /// uncompressed: they are read on every attempt, and compressed ones made scoring a third slower.
    /// </summary>
    /// <param name="name">The output file in <paramref name="work"/>.</param>
    private async Task<string> NormalizeAsync(string source, ImageFormat format, string work, IEnumerable<string> sizeArguments,
        CancellationToken ct, string name = "reference.png")
    {
        var input = source;
        if (format == ImageFormat.Avif)
        {
            // avifdec applies the irot/imir transforms. Its PNG can carry a cICP chunk that ssimulacra2 can't read
            // (an "unspecified" transfer function), so vipsthumbnail rewrites it either way.
            input = Path.Combine(work, "decoded.png");
            var avifdec = await tools.RunAsync("avifdec", ["--png-compress", "0", source, input], ct);
            if (avifdec.ExitCode != 0 || !File.Exists(input))
                throw new UnsupportedImageException("Could not decode the AVIF image.");
        }
        else if (format == ImageFormat.Jxl)
        {
            // djxl applies the codestream orientation and writes no EXIF, so vipsthumbnail doesn't rotate it again.
            input = Path.Combine(work, "decoded.png");
            var djxl = await tools.RunAsync("djxl", [source, input, "--quiet"], ct);
            if (djxl.ExitCode != 0 || !File.Exists(input))
                throw new UnsupportedImageException("Could not decode the JPEG XL image.");
        }

        var output = Path.Combine(work, name);
        var vips = await tools.RunAsync("vipsthumbnail",
            [input, .. sizeArguments, "-o", $"{output}[keep=icc,compression=0]"], ct);

        if (vips.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException($"vipsthumbnail exited {vips.ExitCode}: {vips.StdErr}");

        return output;
    }

    /// <summary>
    /// Cuts <paramref name="rect"/> out of the upright image as a lossless PNG, keeping only the ICC profile. The
    /// vips CLI reads JPEG, PNG and WebP but doesn't apply the orientation tag, and the rectangle is in display
    /// pixels, so an AVIF, a JPEG XL or a rotated image is made upright first.
    /// </summary>
    private async Task<string> CropAsync(string source, ImageFormat format, ImageInfo info, CropRect rect, string work,
        CancellationToken ct)
    {
        var input = info.Orientation == 1 && format is not (ImageFormat.Avif or ImageFormat.Jxl)
            ? source
            : await NormalizeAsync(source, format, work, ["--size", $"{info.DisplayWidth}x"], ct, "upright.png");

        var output = Path.Combine(work, "cropped.png");
        var vips = await tools.RunAsync("vips",
            ["extract_area", input, $"{output}[keep=icc,compression=0]", .. rect.VipsArguments()], ct);

        if (vips.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException($"vips extract_area exited {vips.ExitCode}: {vips.StdErr}");

        return output;
    }

    /// <summary>Composites a PNG with alpha onto white, for JPEG output.</summary>
    private async Task<string> FlattenAsync(string reference, string work, int bitDepth, CancellationToken ct)
    {
        var output = Path.Combine(work, "flattened.png");
        var white = bitDepth == 16 ? "65535" : "255";
        var vips = await tools.RunAsync("vips",
            ["flatten", reference, $"{output}[keep=icc,compression=0]", "--background", white], ct);

        if (vips.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException($"vips flatten exited {vips.ExitCode}: {vips.StdErr}");

        return output;
    }

    private static async Task<byte[]> ReadHeadAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        var buffer = new byte[(int)Math.Min(file.Length, PngHeadBytes)];
        await file.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    /// <param name="keepSource">The source is a JPEG at its original size: jpegtran can recompress it without loss.</param>
    private async Task OptimizeJpegAsync(Job job, List<Candidate> candidates, bool keepSource, CancellationToken ct)
    {
        if (keepSource)
        {
            // Stripping EXIF drops the orientation tag, so rotated phone photos would display sideways.
            // Keep all metadata when the image relies on it; otherwise keep only the ICC profile.
            var copy = job.Info.Orientation == 1 ? "icc" : "all";
            var lossless = Path.Combine(job.Work, "lossless.jpg");
            var jpegtran = await tools.RunAsync("jpegtran",
                ["-copy", copy, "-optimize", "-progressive", "-outfile", lossless, job.Source], ct);

            if (jpegtran.ExitCode == 0 && File.Exists(lossless))
                candidates.Add(new("jpegtran", lossless, new FileInfo(lossless).Length, null));
            else
                logger.LogWarning("jpegtran exited {ExitCode}: {StdErr}", jpegtran.ExitCode, jpegtran.StdErr);
        }

        if (job.MinScore is not { } minScore)
            return;

        var range = new SearchRange(
            Start: Math.Clamp(0.9 + (90 - minScore) * 0.1, options.MinJpegDistance, options.MaxJpegDistance),
            Min: options.MinJpegDistance, Max: options.MaxJpegDistance,
            Step: options.SearchStep, Precision: options.SearchPrecision, Smaller: 1, Integer: false);

        var found = await SearchAsync("jpegli", range, minScore, (d, suffix) => EncodeJpegliAsync(job, d, suffix, ct), ct);
        if (found is not null)
        {
            candidates.Add(found);
            return;
        }

        // Without a lossless candidate (resized or converted), keep the gentlest encode even though it missed the floor.
        if (!keepSource)
        {
            var fallback = await EncodeJpegliAsync(job, options.MinJpegDistance, "fallback", ct);
            if (fallback is not null)
                candidates.Add(fallback);
        }
    }

    private async Task<Candidate?> EncodeJpegliAsync(Job job, double distance, string suffix, CancellationToken ct)
    {
        var d = distance.ToString("0.###", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"jpegli-{suffix}.jpg");
        var cjpegli = await tools.RunAsync("cjpegli", [job.Reference, output, $"--distance={d}"], ct);
        if (cjpegli.ExitCode != 0 || !File.Exists(output))
        {
            logger.LogWarning("cjpegli exited {ExitCode}: {StdErr}", cjpegli.ExitCode, cjpegli.StdErr);
            return null;
        }

        var score = await ScoreAsync(job.Reference, output, ct);
        return new(MethodName(job, $"jpegli-d{d}"), output, new FileInfo(output).Length, score);
    }

    /// <param name="tryLossless">Also encode losslessly and keep it if smaller; graphics often compress better that way.</param>
    private async Task OptimizeWebPAsync(Job job, List<Candidate> candidates, bool lossless, bool tryLossless, CancellationToken ct)
    {
        Candidate? found = null;
        if (!lossless && job.MinScore is { } minScore)
        {
            var range = new SearchRange(
                Start: Math.Clamp(Math.Round(minScore + 9), options.MinWebPQuality, 100),
                Min: options.MinWebPQuality, Max: 100,
                Step: options.QualitySearchStep, Precision: 1, Smaller: -1, Integer: true);
            found = await SearchAsync("cwebp", range, minScore, (q, suffix) => EncodeWebPAsync(job, q, suffix, ct), ct);
            if (found is not null)
                candidates.Add(found);
        }

        // Lossy WebP is 4:2:0 only, so some images never reach a high floor; lossless is the fallback.
        if (tryLossless || found is null)
        {
            var output = Path.Combine(job.Work, "lossless.webp");
            var cwebp = await tools.RunAsync("cwebp",
            [
                "-quiet", "-lossless", "-exact", "-z", options.WebPLosslessEffort.ToString(CultureInfo.InvariantCulture),
                "-metadata", "icc", job.Reference, "-o", output
            ], ct);

            if (cwebp.ExitCode == 0 && File.Exists(output))
                candidates.Add(new(MethodName(job, "cwebp-lossless"), output, new FileInfo(output).Length, null));
            else
                logger.LogWarning("cwebp exited {ExitCode}: {StdErr}", cwebp.ExitCode, cwebp.StdErr);
        }
    }

    private async Task<Candidate?> EncodeWebPAsync(Job job, double quality, string suffix, CancellationToken ct)
    {
        var q = quality.ToString("0", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"webp-{suffix}.webp");
        var cwebp = await tools.RunAsync("cwebp",
            ["-quiet", "-m", "4", "-sharp_yuv", "-q", q, "-metadata", "icc", job.Reference, "-o", output], ct);
        if (cwebp.ExitCode != 0 || !File.Exists(output))
        {
            logger.LogWarning("cwebp exited {ExitCode}: {StdErr}", cwebp.ExitCode, cwebp.StdErr);
            return null;
        }

        // ssimulacra2 can't read WebP; vips decodes it and keeps the ICC profile (dwebp drops it).
        var decoded = Path.Combine(job.Work, $"webp-{suffix}.png");
        var vips = await tools.RunAsync("vips", ["copy", output, $"{decoded}[keep=icc,compression=0]"], ct);
        var score = vips.ExitCode == 0 && File.Exists(decoded) ? await ScoreAsync(job.Reference, decoded, ct) : null;
        return new(MethodName(job, $"cwebp-q{q}"), output, new FileInfo(output).Length, score);
    }

    private async Task OptimizeAvifAsync(Job job, List<Candidate> candidates, bool lossless, CancellationToken ct)
    {
        if (!lossless && job.MinScore is { } minScore)
        {
            var range = new SearchRange(
                Start: Math.Clamp(Math.Round(minScore + 2), options.MinAvifQuality, 100),
                Min: options.MinAvifQuality, Max: 100,
                Step: options.QualitySearchStep, Precision: 1, Smaller: -1, Integer: true);
            var found = await SearchAsync("avifenc", range, minScore, (q, suffix) => EncodeAvifAsync(job, q, suffix, ct), ct);
            if (found is not null)
            {
                candidates.Add(found);
                return;
            }
        }

        var output = Path.Combine(job.Work, "lossless.avif");
        if (await RunAvifencAsync(job, ["-l"], output, lossless: true, ct))
            candidates.Add(new(MethodName(job, "avifenc-lossless"), output, new FileInfo(output).Length, null));
    }

    private async Task<Candidate?> EncodeAvifAsync(Job job, double quality, string suffix, CancellationToken ct)
    {
        var q = quality.ToString("0", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"avif-{suffix}.avif");
        if (!await RunAvifencAsync(job, ["-q", q], output, lossless: false, ct))
            return null;

        var decoded = Path.Combine(job.Work, $"avif-{suffix}.png");
        var avifdec = await tools.RunAsync("avifdec", ["--png-compress", "0", output, decoded], ct);
        var score = avifdec.ExitCode == 0 && File.Exists(decoded) ? await ScoreAsync(job.Reference, decoded, ct) : null;
        return new(MethodName(job, $"avifenc-q{q}"), output, new FileInfo(output).Length, score);
    }

    private async Task<bool> RunAvifencAsync(Job job, string[] quality, string output, bool lossless, CancellationToken ct)
    {
        List<string> args = ["-s", options.AvifSpeed.ToString(CultureInfo.InvariantCulture), "--ignore-exif", "--ignore-xmp", .. quality];

        // Without an ICC profile the pixels are sRGB: say so, or the decoded PNG has an "unspecified" transfer
        // function that ssimulacra2 (and some viewers) can't interpret. Lossless needs the identity matrix (0).
        if (!job.Icc)
            args.AddRange(["--cicp", lossless ? "1/13/0" : "1/13/6"]);

        args.AddRange([job.Reference, output]);

        var avifenc = await tools.RunAsync("avifenc", args, ct);
        if (avifenc.ExitCode == 0 && File.Exists(output))
            return true;

        logger.LogWarning("avifenc exited {ExitCode}: {StdErr}", avifenc.ExitCode, avifenc.StdErr);
        return false;
    }

    /// <param name="tryLossless">Also encode losslessly and keep it if smaller; graphics often compress better that way.</param>
    /// <param name="recompressJpeg">
    /// The source is a JPEG at its original size: cjxl can recompress its DCT data without loss, typically about 20%
    /// smaller with identical pixels.
    /// </param>
    private async Task OptimizeJxlAsync(Job job, List<Candidate> candidates, bool lossless, bool tryLossless,
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
                recompressed = new("cjxl-jpeg", output, new FileInfo(output).Length, null);
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
            found = await SearchAsync("cjxl", range, minScore, (d, suffix) => EncodeJxlAsync(job, d, suffix, ct), ct);
            if (found is not null)
                candidates.Add(found);
        }

        // Lossless from the pixels: asked for, worth a try for graphics, or the fallback when nothing else worked.
        if (tryLossless || (found is null && recompressed is null))
        {
            var output = Path.Combine(job.Work, "lossless.jxl");
            if (await RunCjxlAsync(job.Reference, output, ["-d", "0"], ct))
                candidates.Add(new(MethodName(job, "cjxl-lossless"), output, new FileInfo(output).Length, null));
        }
    }

    private async Task<Candidate?> EncodeJxlAsync(Job job, double distance, string suffix, CancellationToken ct)
    {
        var d = distance.ToString("0.###", CultureInfo.InvariantCulture);
        var output = Path.Combine(job.Work, $"jxl-{suffix}.jxl");

        // cjxl recompresses a JPEG input losslessly unless told not to, whatever the distance; the reference is the
        // source JPEG itself when nothing else changes.
        if (!await RunCjxlAsync(job.Reference, output, ["-d", d, "--lossless_jpeg=0"], ct))
            return null;

        // Scored from djxl's PNG, the pixels a viewer shows; ssimulacra2 reading the .jxl itself scores a little higher.
        var decoded = Path.Combine(job.Work, $"jxl-{suffix}.png");
        var djxl = await tools.RunAsync("djxl", [output, decoded, "--quiet"], ct);
        var score = djxl.ExitCode == 0 && File.Exists(decoded) ? await ScoreAsync(job.Reference, decoded, ct) : null;
        return new(MethodName(job, $"cjxl-d{d}"), output, new FileInfo(output).Length, score);
    }

    private async Task<bool> RunCjxlAsync(string input, string output, string[] arguments, CancellationToken ct)
    {
        var cjxl = await tools.RunAsync("cjxl",
            [input, output, .. arguments, "-e", options.JxlEffort.ToString(CultureInfo.InvariantCulture), "--quiet"], ct);
        if (cjxl.ExitCode == 0 && File.Exists(output))
            return true;

        logger.LogWarning("cjxl exited {ExitCode}: {StdErr}", cjxl.ExitCode, cjxl.StdErr);
        return false;
    }

    /// <summary>
    /// Finds the encoder setting with the smallest result that still meets the floor. Steps from a guess toward
    /// smaller files while results pass (or toward larger while they fail), doubling the step while the direction
    /// holds, until the answer is bracketed; then interpolates between the two scores, which change almost
    /// linearly with the setting over a short range. Scoring dominates the cost (about 2 s per attempt at 15 MP),
    /// so this typically takes 3-4 attempts.
    /// </summary>
    private async Task<Candidate?> SearchAsync(string encoder, SearchRange range, double minScore,
        Func<double, string, Task<Candidate?>> encode, CancellationToken ct)
    {
        double? passing = null, failing = null;
        double passingScore = 0, failingScore = 0;
        var value = range.Start;
        var stride = range.Step;
        bool? lastPassed = null;
        Candidate? best = null;

        for (var step = 0; step < options.MaxSearchSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var attempt = await encode(value, step.ToString(CultureInfo.InvariantCulture));
            if (attempt is null)
                break;

            var passed = attempt.Score >= minScore;
            logger.LogDebug("{Encoder} {Value:0.###}: {Bytes} bytes, score {Score:F2} ({Result})",
                encoder, value, attempt.Size, attempt.Score, passed ? "pass" : "fail");

            if (passed)
            {
                if (best is null || attempt.Size < best.Size)
                    best = attempt;
                passing = value;
                passingScore = attempt.Score!.Value;
            }
            else
            {
                failing = value;
                failingScore = attempt.Score ?? 0;
            }

            double next;
            if (passing is { } p && failing is { } f)
            {
                if (Math.Abs(f - p) <= range.Precision)
                    break;

                // Where the floor falls between the two scores, rounded toward the passing side.
                var fraction = passingScore > failingScore ? (minScore - failingScore) / (passingScore - failingScore) : 0.5;
                next = f + Math.Clamp(fraction, 0.1, 0.9) * (p - f);
                if (range.Integer)
                    next = p > f ? Math.Ceiling(next) : Math.Floor(next);
                if (next == p || next == f)
                    next = range.Integer ? (p > f ? p - 1 : p + 1) : (p + f) / 2;
            }
            else
            {
                if (lastPassed == passed)
                    stride *= 2;
                next = value + (passed ? range.Smaller : -range.Smaller) * stride;
            }

            lastPassed = passed;

            next = Math.Clamp(next, range.Min, range.Max);
            if (Math.Abs(next - value) < 0.001 || next == passing || next == failing)
                break;

            value = next;
        }

        return best;
    }

    private static string MethodName(Job job, string method) => (job.Cropped ? "crop+" : "") + (job.Resized ? "resize+" : "") + method;

    private async Task OptimizePngAsync(Job job, List<Candidate> candidates, CancellationToken ct)
    {
        // Lossless baseline: oxipng on the original, or on the resized pixels
        var lossless = await OxipngAsync(job.Reference, Path.Combine(job.Work, "lossless.png"), alpha: false, ct);
        if (lossless is not null)
            candidates.Add(new(MethodName(job, "oxipng"), lossless, new FileInfo(lossless).Length, null));
        else if (job.Reference != job.Source)
            candidates.Add(new(MethodName(job, "") is { Length: > 0 } changed ? changed.TrimEnd('+') : "decode",
                job.Reference, new FileInfo(job.Reference).Length, null));

        if (job.MinScore is not { } minScore)
            return;

        var quantized = await QuantizeAsync(job, minScore, ct);
        if (quantized is null)
            return;

        // Full-strength recompression of the winner only; --alpha rewrites the RGB of fully transparent
        // pixels: invisible, but not pixel-identical.
        var optimized = await OxipngAsync(quantized.Path, Path.Combine(job.Work, "quantized-opt.png"), alpha: true, ct);
        var method = MethodName(job, "pngquant+oxipng");
        candidates.Add(optimized is not null && new FileInfo(optimized).Length < quantized.Size
            ? new(method, optimized, new FileInfo(optimized).Length, quantized.Score)
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
    private async Task<Candidate?> QuantizeAsync(Job job, double minScore, CancellationToken ct)
    {
        Candidate? best = null;
        foreach (var maxQuality in PngQuantMaxQualities.Take(options.MaxPngQuantAttempts))
        {
            var output = Path.Combine(job.Work, $"quantized-{maxQuality}.png");
            var pngquant = await tools.RunAsync("pngquant",
                ["--quality", $"0-{maxQuality}", "--speed", "1", "--force", "--output", output, "--", job.Reference], ct);

            if (pngquant.ExitCode != 0 || !File.Exists(output))
            {
                logger.LogWarning("pngquant exited {ExitCode}: {StdErr}", pngquant.ExitCode, pngquant.StdErr);
                break;
            }

            var score = await ScoreAsync(job.Reference, output, ct);
            logger.LogDebug("pngquant 0-{MaxQuality}: {Bytes} bytes, score {Score:F2}", maxQuality, new FileInfo(output).Length, score);

            if (!(score >= minScore))
                break;

            var quick = await OxipngAsync(output, Path.Combine(job.Work, $"quantized-{maxQuality}-quick.png"), alpha: true, ct, level: "1");
            var candidate = quick is not null
                ? new Candidate("pngquant", quick, new FileInfo(quick).Length, score)
                : new Candidate("pngquant", output, new FileInfo(output).Length, score);

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

        var oxipng = await tools.RunAsync("oxipng", args, ct);
        if (oxipng.ExitCode == 0 && File.Exists(output))
            return output;

        logger.LogWarning("oxipng exited {ExitCode}: {StdErr}", oxipng.ExitCode, oxipng.StdErr);
        return null;
    }

    /// <summary>SSIMULACRA2 score of candidate vs. reference (100 = identical, 90+ = visually lossless).</summary>
    private async Task<double?> ScoreAsync(string reference, string candidate, CancellationToken ct)
    {
        var result = await tools.RunAsync("ssimulacra2", [reference, candidate], ct);
        if (result.ExitCode == 0
            && double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
            return score;

        logger.LogWarning("ssimulacra2 exited {ExitCode}: {StdErr}", result.ExitCode, result.StdErr);
        return null;
    }
}
