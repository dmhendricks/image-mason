using System.Globalization;

namespace ImageMason.Imaging;

/// <param name="Reference">The pixels results are scored against: the source, or a normalized PNG.</param>
/// <param name="Input">The pixels results are encoded from; see <see cref="OptimizeJob.Input"/>.</param>
/// <param name="Icc">The reference embeds an ICC profile.</param>
internal sealed record PreparedPixels(string Reference, string Input, bool Icc);

/// <summary>
/// Turns a job's source into the pixels it encodes and scores: cropped, resized and made upright as asked, decoded to
/// PNG when the encoders or the scorer can't read the source, and flattened onto white for JPEG.
/// </summary>
internal sealed class Preprocessor(ToolRunner tools, HeicDecoder heic, ILogger logger)
{
    // Enough of a PNG to reach the first IDAT, where the iCCP and tRNS chunks end.
    private const int PngHeadBytes = 4 * 1024 * 1024;

    private readonly JobTools _tools = new(tools, logger);

    /// <param name="input">The source's bytes, already written to <paramref name="source"/>.</param>
    /// <param name="crop">The area to cut out; null keeps the whole image.</param>
    /// <param name="resize">The box to resize the (cropped) image to; null keeps its size.</param>
    /// <param name="rotate">Apply the orientation tag.</param>
    /// <param name="scored">Results will be scored against the reference.</param>
    public async Task<PreparedPixels> PrepareAsync(string work, string source, byte[] input, ImageFormat format, ImageInfo info,
        CropRect? crop, ResizeRequest? resize, bool rotate, ImageFormat target, bool scored, CancellationToken ct)
    {
        // The encoders and the scorer read PNG and JPEG; oxipng and pngquant read PNG only.
        var normalize = resize is not null || rotate
            || format is ImageFormat.WebP or ImageFormat.Avif or ImageFormat.Jxl or ImageFormat.Heic
            || (target == ImageFormat.Png && format != ImageFormat.Png);
        string reference;
        if (crop is not null)
        {
            reference = await CropAsync(source, format, info, crop, work, ct);
            if (resize is not null)
                reference = await NormalizeAsync(reference, ImageFormat.Png, work, resize.VipsArguments(), ct);
        }
        else
        {
            reference = normalize
                ? await NormalizeAsync(source, format, work,
                    resize is not null ? resize.VipsArguments() : ["--size", $"{info.DisplayWidth}x"], ct)
                : source;
        }

        var referenceFormat = reference == source ? format : ImageFormat.Png;
        var head = reference == source ? input : await ReadHeadAsync(reference, ct);

        var hasAlpha = referenceFormat == ImageFormat.Png && ImageHeader.PngHasAlpha(head);

        // JPEG has no alpha: flatten onto white, which is also what the result is scored against.
        if (target == ImageFormat.Jpeg && hasAlpha)
        {
            reference = await FlattenAsync(reference, work, ImageHeader.PngBitDepth(head), ct);
            head = await ReadHeadAsync(reference, ct);
            hasAlpha = false;
        }

        // Scoring an image with alpha takes two to three times as long (it is also scored on a dark and a light
        // background), so an alpha channel that is opaque everywhere is dropped from what results are scored
        // against; the scores are the same. The encoders keep their input, so the results don't change.
        var encoderInput = reference;
        if (hasAlpha && scored && await IsOpaqueAsync(reference, work, ImageHeader.PngBitDepth(head), ct))
            reference = await FlattenAsync(reference, work, ImageHeader.PngBitDepth(head), ct);

        return new(reference, encoderInput, ImageHeader.HasIccProfile(head, referenceFormat));
    }

    /// <summary>
    /// A copy of the image for a client that can't display its format, such as a browser and HEIC: upright, fitted
    /// within <paramref name="maxSide"/> pixels (never enlarged), as WebP, which every current browser shows and which
    /// keeps transparency.
    /// </summary>
    public async Task<string> PreviewAsync(string work, string source, ImageFormat format, int maxSide, CancellationToken ct)
    {
        var failed = $"Could not decode the {format.DisplayName()} image. It may be damaged.";
        var input = await DecodeAsync(source, format, work, failed, ct);
        var output = Path.Combine(work, "preview.webp");
        await _tools.RequireOutputAsync("vipsthumbnail", [input, "--size", $"{maxSide}x{maxSide}>", "-o", $"{output}[Q=85,keep=icc]"],
            output, failed, ct);
        return output;
    }

    /// <summary>
    /// Writes the pixels the encoders start from as a lossless PNG, resized by <paramref name="sizeArguments"/> (a
    /// no-op size just bakes in the orientation), keeping only the ICC profile. Work PNGs are uncompressed: they are
    /// read on every attempt, and compressed ones made scoring a third slower.
    /// </summary>
    /// <param name="name">The output file in <paramref name="work"/>.</param>
    private async Task<string> NormalizeAsync(string source, ImageFormat format, string work, IEnumerable<string> sizeArguments,
        CancellationToken ct, string name = "reference.png")
    {
        var failed = $"Could not decode the {format.DisplayName()} image. It may be damaged.";
        var input = await DecodeAsync(source, format, work, failed, ct);

        var output = Path.Combine(work, name);
        await _tools.RequireOutputAsync("vipsthumbnail", [input, .. sizeArguments, "-o", $"{output}[keep=icc,compression=0]"],
            output, failed, ct);

        return output;
    }

    /// <summary>
    /// The file vipsthumbnail reads the image from: the source itself, or for the formats it can't read, an upright
    /// uncompressed PNG decoded by their own tool (avifdec, djxl, heif-dec).
    /// </summary>
    private async Task<string> DecodeAsync(string source, ImageFormat format, string work, string failed, CancellationToken ct)
    {
        var decoded = Path.Combine(work, "decoded.png");
        switch (format)
        {
            case ImageFormat.Avif:
                // avifdec applies the irot/imir transforms. Its PNG can carry a cICP chunk with an "unspecified"
                // transfer function that some tools can't read, so vipsthumbnail rewrites it either way.
                await _tools.RequireOutputAsync("avifdec", ["--png-compress", "0", source, decoded], decoded, failed, ct);
                return decoded;
            case ImageFormat.Jxl:
                // djxl applies the codestream orientation and writes no EXIF, so vipsthumbnail doesn't rotate it again.
                await _tools.RequireOutputAsync("djxl", [source, decoded, "--quiet"], decoded, failed, ct);
                return decoded;
            case ImageFormat.Heic:
                // heif-dec applies the irot/imir transforms and sets the EXIF orientation it writes to 1, so vipsthumbnail
                // doesn't rotate it again. It keeps libheif's limits on image size, which bound its memory.
                heic.EnsureReady();
                await _tools.RequireOutputAsync(heic.DecoderPath, ["--quiet", "--png-compression-level", "0", source, decoded],
                    decoded, failed, ct, heic.Environment);
                return decoded;
            default:
                return source;
        }
    }

    /// <summary>
    /// Cuts <paramref name="rect"/> out of the upright image as a lossless PNG, keeping only the ICC profile. The
    /// vips CLI reads JPEG, PNG and WebP but doesn't apply the orientation tag, and the rectangle is in display
    /// pixels, so an AVIF, a JPEG XL, a HEIC or a rotated image is made upright first.
    /// </summary>
    private async Task<string> CropAsync(string source, ImageFormat format, ImageInfo info, CropRect rect, string work,
        CancellationToken ct)
    {
        var input = info.Orientation == 1 && format is not (ImageFormat.Avif or ImageFormat.Jxl or ImageFormat.Heic)
            ? source
            : await NormalizeAsync(source, format, work, ["--size", $"{info.DisplayWidth}x"], ct, "upright.png");

        var output = Path.Combine(work, "cropped.png");
        await _tools.RequireOutputAsync("vips", ["extract_area", input, $"{output}[keep=icc,compression=0]", .. rect.VipsArguments()],
            output, $"Could not crop the {format.DisplayName()} image. It may be damaged.", ct);

        return output;
    }

    /// <summary>Composites a PNG with alpha onto white, for JPEG output.</summary>
    private async Task<string> FlattenAsync(string reference, string work, int bitDepth, CancellationToken ct)
    {
        var output = Path.Combine(work, "flattened.png");
        var white = bitDepth == 16 ? "65535" : "255";
        await _tools.RequireOutputAsync("vips", ["flatten", reference, $"{output}[keep=icc,compression=0]", "--background", white],
            output, "Could not remove the image's transparency for JPEG.", ct);

        return output;
    }

    /// <summary>
    /// True when a PNG's alpha channel is at its maximum everywhere. vips stats writes one row per band after the
    /// all-bands row, alpha last, with the minimum first. False when the check fails, which only costs speed.
    /// </summary>
    private async Task<bool> IsOpaqueAsync(string png, string work, int bitDepth, CancellationToken ct)
    {
        var output = Path.Combine(work, "stats.mat");
        if (!await _tools.RunForOutputAsync("vips", ["stats", png, output], output, ct))
            return false;

        var rows = (await File.ReadAllLinesAsync(output, ct)).Where(line => line.Length > 0).ToArray();
        var alphaMin = rows.Length > 2 ? rows[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : null;
        return double.TryParse(alphaMin, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            && min >= (bitDepth == 16 ? 65535 : 255);
    }

    private static async Task<byte[]> ReadHeadAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        var buffer = new byte[(int)Math.Min(file.Length, PngHeadBytes)];
        await file.ReadExactlyAsync(buffer, ct);
        return buffer;
    }
}
