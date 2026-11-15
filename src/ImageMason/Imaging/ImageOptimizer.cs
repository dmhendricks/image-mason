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
/// <param name="Format">Output format. Null keeps the input's format (JPEG for HEIC; see <see cref="ImageFormatDetector.DefaultTarget"/>).</param>
/// <param name="Resize">Resize to a box; see <see cref="ResizeRequest"/>. Applies to the cropped area when <paramref name="Rect"/> is set.</param>
/// <param name="Rect">The area to keep, cut out before any resize.</param>
/// <param name="IsPhoto">For a PNG input, <see cref="PngPhoto.IsPhoto"/> when the caller already checked; checked when
/// needed otherwise.</param>
public sealed record OptimizeRequest(QualityPreset Quality, double? MinScore = null, ImageFormat? Format = null,
    ResizeRequest? Resize = null, CropRect? Rect = null, bool? IsPhoto = null);

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

/// <summary>
/// A recognized image the tools couldn't decode or encode, usually because it's damaged. The message is safe to
/// return; the tool's output is logged instead.
/// </summary>
public sealed class ImageProcessingException(string message) : Exception(message);

/// <summary>A copy of an image to display; see <see cref="ImageOptimizer.PreviewAsync"/>.</summary>
/// <param name="Info">The original's dimensions and orientation, which the copy is smaller than.</param>
public sealed record PreviewResult(byte[] Data, ImageInfo Info);

public sealed class ImageOptimizer(ToolRunner tools, JobMemoryGate memory, ImageMasonOptions options, HeicDecoder heic,
    ILogger<ImageOptimizer> logger)
{
    private const string Original = "original";

    // Peak container memory per pixel, measured on 12-48 megapixel images and rounded up (work files in /tmp count,
    // since tmpfs is memory). Scoring with SSIMULACRA2 dominates: lossy presets need about 10x a lossless pass.
    internal const long ScoringBytesPerPixel = 150;
    internal const long PngLosslessBytesPerPixel = 48;
    internal const long JpegLosslessBytesPerPixel = 16;
    internal const long WebPLosslessBytesPerPixel = 32;
    internal const long AvifLosslessBytesPerPixel = 40;

    // heif-dec alone peaked at 4.5 bytes per pixel (48 MP); the steps after it are the same as for AVIF.
    internal const long HeicLosslessBytesPerPixel = AvifLosslessBytesPerPixel;

    // cjxl alone peaked at 23 bytes per pixel lossless and 33 lossy (effort 7, 12 MP), djxl at 8; a whole lossless
    // JPEG-to-JPEG XL job at 15.6 MP came to 22. Lossy encodes run between scoring passes, which dominate.
    internal const long JxlLosslessBytesPerPixel = 32;
    internal const long JobOverheadBytes = 64L * 1024 * 1024;

    private readonly Preprocessor _preprocessor = new(tools, heic, logger);
    private readonly JpegEncoder _jpeg = new(tools, options, logger);
    private readonly PngEncoder _png = new(tools, options, logger);
    private readonly WebPEncoder _webP = new(tools, options, logger);
    private readonly AvifEncoder _avif = new(tools, options, logger);
    private readonly JxlEncoder _jxl = new(tools, options, logger);

    public async Task<OptimizeResult> OptimizeAsync(byte[] input, OptimizeRequest request, CancellationToken ct)
    {
        var (format, info) = ReadInput(input);
        var target = request.Format ?? format.DefaultTarget();
        var converting = target != format;
        var lossless = request.Quality == QualityPreset.Lossless;
        var rect = request.Rect;
        if (rect is not null && !rect.FitsWithin(info.DisplayWidth, info.DisplayHeight))
            throw new CropOutsideImageException(string.Create(CultureInfo.InvariantCulture,
                $"crop {rect} reaches past the image, which is {info.DisplayWidth}x{info.DisplayHeight} as displayed."));

        // Resizing applies to the cropped area.
        var crop = rect is not null && !rect.Covers(info.DisplayWidth, info.DisplayHeight);
        var (sourceWidth, sourceHeight) = crop ? (rect!.Width, rect.Height) : (info.DisplayWidth, info.DisplayHeight);

        var sizing = request.Resize;
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

        // A PNG graphic is also tried losslessly. Checked at most once, and only now that the pixel limit has passed.
        var isPhoto = request.IsPhoto;
        bool IsPngGraphic() => format == ImageFormat.Png && !(isPhoto ??= PngPhoto.IsPhoto(input, options.MaxPixels, ct));

        var scored = minScore is not null;
        var outputPixels = (long)outputWidth * outputHeight;
        using var reservation = await memory.ReserveAsync(EstimateMemory(format, target, info.PixelCount, outputPixels, scored), ct);

        var work = Directory.CreateTempSubdirectory("image-mason-");
        try
        {
            var source = Path.Combine(work.FullName, "source" + format.Extension());
            await File.WriteAllBytesAsync(source, input, ct);

            var prepared = await _preprocessor.PrepareAsync(work.FullName, source, input, format, info, crop ? rect : null,
                resize ? sizing : null, rotate, target, scored, ct);

            // Ends with the try block, so the scorer has exited before the work folder is deleted.
            await using var scorer = new Scorer(tools, prepared.Reference, logger);
            var job = new OptimizeJob(work.FullName, source, prepared.Reference, prepared.Input, info, crop, resize, minScore,
                prepared.Icc, scorer);

            // Size guard: unless the image must be cropped, resized or converted, the original is a candidate and wins ties.
            List<Candidate> candidates = crop || resize || converting ? [] : [new(Original, source, input.Length, null)];

            switch (target)
            {
                case ImageFormat.Jpeg:
                    await _jpeg.AddCandidatesAsync(job, candidates, keepSource: format == ImageFormat.Jpeg && !crop && !resize, ct);
                    break;
                case ImageFormat.Png:
                    await _png.AddCandidatesAsync(job, candidates, ct);
                    break;
                case ImageFormat.WebP:
                    await _webP.AddCandidatesAsync(job, candidates, lossless,
                        tryLossless: lossless || IsPngGraphic(), ct);
                    break;
                case ImageFormat.Avif:
                    await _avif.AddCandidatesAsync(job, candidates, lossless, ct);
                    break;
                case ImageFormat.Jxl:
                    await _jxl.AddCandidatesAsync(job, candidates, lossless,
                        tryLossless: IsPngGraphic(),
                        recompressJpeg: format == ImageFormat.Jpeg && !crop && !resize, ct);
                    break;
                default:
                    throw new UnsupportedImageException($"Unsupported output format {target}.");
            }

            var best = candidates.MinBy(c => c.Size);
            if (best is null)
            {
                if (converting)
                    throw new ImageProcessingException($"Could not encode the {format.DisplayName()} image as {target.DisplayName()}. It may be damaged.");

                // The original would ignore the requested area.
                if (crop)
                    throw new ImageProcessingException($"Could not encode the cropped {format.DisplayName()} image. It may be damaged.");

                logger.LogWarning("No resized result was produced; returning the original");
                best = new(Original, source, input.Length, null);
            }

            var data = best.Method == Original ? input : await File.ReadAllBytesAsync(best.Path, ct);

            return new OptimizeResult(data, target, input.Length, info, ImageInfo.Read(data, target), best.Method, best.Score, minScore);
        }
        finally
        {
            DeleteWork(work);
        }
    }

    /// <summary>
    /// A copy of the image fitted within <paramref name="maxSide"/> pixels, upright, as WebP: for clients that can't
    /// display the original's format, such as browsers and HEIC. Quick: one decode and one resize, no quality search.
    /// </summary>
    public async Task<PreviewResult> PreviewAsync(byte[] input, int maxSide, CancellationToken ct)
    {
        var (format, info) = ReadInput(input);
        var scale = Math.Min(1, (double)maxSide / Math.Max(info.Width, info.Height));
        var outputPixels = (long)Math.Ceiling(info.PixelCount * scale * scale);
        using var reservation = await memory.ReserveAsync(EstimateMemory(format, ImageFormat.WebP, info.PixelCount, outputPixels, scored: false), ct);

        var work = Directory.CreateTempSubdirectory("image-mason-");
        try
        {
            var source = Path.Combine(work.FullName, "source" + format.Extension());
            await File.WriteAllBytesAsync(source, input, ct);
            var preview = await _preprocessor.PreviewAsync(work.FullName, source, format, maxSide, ct);
            return new PreviewResult(await File.ReadAllBytesAsync(preview, ct), info);
        }
        finally
        {
            DeleteWork(work);
        }
    }

    /// <summary>The input's format and header, refusing what the service can't read before anything is decoded.</summary>
    /// <exception cref="UnsupportedImageException">Not a format the service reads, or animated.</exception>
    /// <exception cref="HeicNotReadyException">A HEIC image while its decoder is still being installed.</exception>
    /// <exception cref="ImageTooLargeException">Over the pixel limit.</exception>
    private (ImageFormat Format, ImageInfo Info) ReadInput(byte[] input)
    {
        var format = ImageFormatDetector.Detect(input);
        if (format == ImageFormat.Unknown)
            throw new UnsupportedImageException($"Unsupported image format; expected {heic.InputNames}.");
        if (format == ImageFormat.Heic)
            heic.EnsureReady();

        var info = ReadInfo(input, format, options.MaxPixels);
        if (info.Animated)
            throw new UnsupportedImageException("Animated images are not supported.");

        return (format, info);
    }

    private void DeleteWork(DirectoryInfo work)
    {
        try
        {
            work.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to delete work directory {Path}", work.FullName);
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

    /// <summary>Peak bytes per pixel of a lossless pass in <paramref name="format"/>; see <see cref="EstimateMemory"/>.</summary>
    internal static long LosslessBytesPerPixel(ImageFormat format) => format switch
    {
        ImageFormat.Png => PngLosslessBytesPerPixel,
        ImageFormat.WebP => WebPLosslessBytesPerPixel,
        ImageFormat.Avif => AvifLosslessBytesPerPixel,
        ImageFormat.Jxl => JxlLosslessBytesPerPixel,
        ImageFormat.Heic => HeicLosslessBytesPerPixel,
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
}
