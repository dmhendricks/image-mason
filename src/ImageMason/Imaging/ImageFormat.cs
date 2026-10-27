using System.Buffers.Binary;

namespace ImageMason.Imaging;

public enum ImageFormat
{
    Unknown,
    Png,
    Jpeg,
    WebP,
    Avif,
    Jxl
}

public static class ImageFormatDetector
{
    /// <summary>Every format the API reads and writes.</summary>
    public static readonly ImageFormat[] Supported =
        [ImageFormat.Png, ImageFormat.WebP, ImageFormat.Jpeg, ImageFormat.Jxl, ImageFormat.Avif];

    /// <summary>"JPEG, PNG, WebP, AVIF or JPEG XL", for error messages.</summary>
    public const string SupportedNames = "JPEG, PNG, WebP, AVIF or JPEG XL";

    // The ISO BMFF container's first box: size 12, type "JXL ", then a fixed 4-byte signature.
    private static ReadOnlySpan<byte> JxlContainer => [0, 0, 0, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>Detects the format from magic bytes; the request Content-Type is not trusted.</summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return ImageFormat.Png;

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return ImageFormat.Jpeg;

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return ImageFormat.WebP;

        if (IsAvif(data))
            return ImageFormat.Avif;

        // A bare codestream starts with FF 0A; the container with its signature box.
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0x0A]) || data.StartsWith(JxlContainer))
            return ImageFormat.Jxl;

        return ImageFormat.Unknown;
    }

    /// <summary>Parses a format name from a request: jpeg or jpg, png, webp, avif, jxl (or jpegxl). Case-insensitive.</summary>
    public static bool TryParse(string? value, out ImageFormat format)
    {
        format = value?.Trim().ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => ImageFormat.Jpeg,
            "png" => ImageFormat.Png,
            "webp" => ImageFormat.WebP,
            "avif" => ImageFormat.Avif,
            "jxl" or "jpegxl" => ImageFormat.Jxl,
            _ => ImageFormat.Unknown
        };
        return format != ImageFormat.Unknown;
    }

    public static string ContentType(this ImageFormat format) => format switch
    {
        ImageFormat.Png => "image/png",
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.WebP => "image/webp",
        ImageFormat.Avif => "image/avif",
        ImageFormat.Jxl => "image/jxl",
        _ => "application/octet-stream"
    };

    public static string Extension(this ImageFormat format) => format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.WebP => ".webp",
        ImageFormat.Avif => ".avif",
        ImageFormat.Jxl => ".jxl",
        _ => ".bin"
    };

    /// <summary>The format a stored file name's extension stands for, or Unknown.</summary>
    public static ImageFormat FromExtension(ReadOnlySpan<char> extension) => extension switch
    {
        ".png" => ImageFormat.Png,
        ".jpg" => ImageFormat.Jpeg,
        ".webp" => ImageFormat.WebP,
        ".avif" => ImageFormat.Avif,
        ".jxl" => ImageFormat.Jxl,
        _ => ImageFormat.Unknown
    };

    /// <summary>Lowercase name used in responses and logs: jpeg, png, webp, avif, jxl.</summary>
    public static string Name(this ImageFormat format) => format.ToString().ToLowerInvariant();

    /// <summary>The name people write, for error messages: JPEG, PNG, WebP, AVIF, JPEG XL.</summary>
    public static string DisplayName(this ImageFormat format) => format switch
    {
        ImageFormat.Png => "PNG",
        ImageFormat.Jpeg => "JPEG",
        ImageFormat.WebP => "WebP",
        ImageFormat.Avif => "AVIF",
        ImageFormat.Jxl => "JPEG XL",
        _ => "unknown"
    };

    // ISO BMFF: the first box is ftyp, with "avif" (still image) or "avis" (sequence) as a brand.
    private static bool IsAvif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[4..8].SequenceEqual("ftyp"u8))
            return false;

        var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), (uint)data.Length);
        if (size < 16)
            return false;

        // Major brand at 8, minor version at 12, compatible brands from 16.
        for (var offset = 8; offset + 4 <= size; offset += offset == 8 ? 8 : 4)
        {
            var brand = data.Slice(offset, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Stored pixel dimensions plus the orientation (1 = normal, 2-8 = viewer must rotate/flip, as in EXIF).
/// Read from the file header only; nothing is decoded.
/// </summary>
/// <param name="Animated">An animated WebP or JPEG XL, or an AVIF image sequence; the API handles still images only.</param>
public readonly record struct ImageInfo(int Width, int Height, int Orientation, bool Animated = false)
{
    /// <summary>Width as displayed: orientations 5-8 rotate by 90 degrees, swapping width and height.</summary>
    public int DisplayWidth => Orientation >= 5 ? Height : Width;

    public int DisplayHeight => Orientation >= 5 ? Width : Height;

    public long PixelCount => (long)Width * Height;

    /// <summary>Read from the header of a file in <paramref name="format"/>; default when it can't be read.</summary>
    public static ImageInfo Read(ReadOnlySpan<byte> data, ImageFormat format) => format switch
    {
        ImageFormat.Png => PngHeader.Read(data),
        ImageFormat.Jpeg => JpegHeader.Read(data),
        ImageFormat.WebP => WebPHeader.Read(data),
        ImageFormat.Avif => AvifHeader.Read(data),
        ImageFormat.Jxl => JxlHeader.Read(data),
        _ => default
    };
}

/// <param name="BitDepth">Bits per channel: 8 for most images; 16 for some PNGs, 10 or 12 for some AVIFs.</param>
/// <param name="HasAlpha">An alpha channel (or, for PNG, a tRNS transparency chunk).</param>
/// <param name="HasIccProfile">An embedded ICC color profile.</param>
public readonly record struct ImageDetails(int BitDepth, bool HasAlpha, bool HasIccProfile);

/// <summary>Header details the pipeline needs before choosing tools, read without decoding.</summary>
public static class ImageHeader
{
    public static ImageDetails ReadDetails(ReadOnlySpan<byte> data, ImageFormat format) => format switch
    {
        ImageFormat.Png => PngHeader.ReadDetails(data),
        ImageFormat.Jpeg => JpegHeader.ReadDetails(data),
        ImageFormat.WebP => WebPHeader.ReadDetails(data),
        ImageFormat.Avif => AvifHeader.ReadDetails(data),
        ImageFormat.Jxl => JxlHeader.ReadDetails(data),
        _ => new(8, false, false)
    };

    /// <summary>True when the file embeds an ICC color profile (PNG iCCP, JPEG APP2 ICC_PROFILE).</summary>
    public static bool HasIccProfile(ReadOnlySpan<byte> data, ImageFormat format) => format switch
    {
        ImageFormat.Png => PngHeader.HasIcc(data),
        ImageFormat.Jpeg => JpegHeader.HasIcc(data),
        _ => false
    };

    /// <summary>True when a PNG has an alpha channel or a tRNS chunk. Works on the first part of a file.</summary>
    public static bool PngHasAlpha(ReadOnlySpan<byte> png) => PngHeader.HasAlpha(png);

    /// <summary>Bits per channel from the PNG IHDR: 1, 2, 4, 8 or 16.</summary>
    public static int PngBitDepth(ReadOnlySpan<byte> png) => PngHeader.BitDepth(png);
}
