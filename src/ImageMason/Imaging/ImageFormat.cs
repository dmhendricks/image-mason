using System.Buffers.Binary;

namespace ImageMason.Imaging;

public enum ImageFormat
{
    Unknown,
    Png,
    Jpeg,
    WebP,
    Avif
}

public static class ImageFormatDetector
{
    /// <summary>Every format the API reads and writes.</summary>
    public static readonly ImageFormat[] Supported = [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP, ImageFormat.Avif];

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

        return ImageFormat.Unknown;
    }

    /// <summary>Parses a format name from a request: jpeg or jpg, png, webp, avif. Case-insensitive.</summary>
    public static bool TryParse(string? value, out ImageFormat format)
    {
        format = value?.Trim().ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => ImageFormat.Jpeg,
            "png" => ImageFormat.Png,
            "webp" => ImageFormat.WebP,
            "avif" => ImageFormat.Avif,
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
        _ => "application/octet-stream"
    };

    public static string Extension(this ImageFormat format) => format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.WebP => ".webp",
        ImageFormat.Avif => ".avif",
        _ => ".bin"
    };

    /// <summary>The format a stored file name's extension stands for, or Unknown.</summary>
    public static ImageFormat FromExtension(ReadOnlySpan<char> extension) => extension switch
    {
        ".png" => ImageFormat.Png,
        ".jpg" => ImageFormat.Jpeg,
        ".webp" => ImageFormat.WebP,
        ".avif" => ImageFormat.Avif,
        _ => ImageFormat.Unknown
    };

    /// <summary>Lowercase name used in responses and logs: jpeg, png, webp, avif.</summary>
    public static string Name(this ImageFormat format) => format.ToString().ToLowerInvariant();

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
/// <param name="Animated">An animated WebP or an AVIF image sequence; the API handles still images only.</param>
public readonly record struct ImageInfo(int Width, int Height, int Orientation, bool Animated = false)
{
    /// <summary>Width as displayed: orientations 5-8 rotate by 90 degrees, swapping width and height.</summary>
    public int DisplayWidth => Orientation >= 5 ? Height : Width;

    public int DisplayHeight => Orientation >= 5 ? Width : Height;

    public long PixelCount => (long)Width * Height;

    public static ImageInfo Read(ReadOnlySpan<byte> data, ImageFormat format) => format switch
    {
        ImageFormat.Png => ReadPng(data),
        ImageFormat.Jpeg => ReadJpeg(data),
        ImageFormat.WebP => WebPHeader.Read(data),
        ImageFormat.Avif => AvifHeader.Read(data),
        _ => default
    };

    // IHDR is always the first chunk: 8-byte signature, 4-byte length, "IHDR", width, height.
    private static ImageInfo ReadPng(ReadOnlySpan<byte> png) =>
        png.Length < 24
            ? default
            : new((int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]), 1);

    private static ImageInfo ReadJpeg(ReadOnlySpan<byte> jpeg)
    {
        int width = 0, height = 0, orientation = 1;

        var i = 2;
        while (i + 4 <= jpeg.Length && jpeg[i] == 0xFF)
        {
            var marker = jpeg[i + 1];

            // Fill byte before a marker
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            // Start of scan / end of image: no more header segments
            if (marker is 0xDA or 0xD9)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(i + 2)..]);
            if (length < 2 || i + 2 + length > jpeg.Length)
                break;

            var segment = jpeg.Slice(i + 4, length - 2);

            if (marker == 0xE1 && segment.StartsWith("Exif\0\0"u8))
                orientation = ExifOrientation.ReadTiff(segment[6..]);

            // SOF0-SOF15, excluding DHT (C4), JPG (C8) and DAC (CC): precision, height, width
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && segment.Length >= 5)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
            }

            i += 2 + length;
        }

        return new(width, height, orientation);
    }
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
        ImageFormat.Png => new(PngBitDepth(data), PngHasAlpha(data), PngChunks(data).Icc),
        ImageFormat.Jpeg => new(JpegPrecision(data), false, JpegHasIcc(data)),
        ImageFormat.WebP => WebPHeader.ReadDetails(data),
        ImageFormat.Avif => AvifHeader.ReadDetails(data),
        _ => new(8, false, false)
    };

    /// <summary>True when the file embeds an ICC color profile (PNG iCCP, JPEG APP2 ICC_PROFILE).</summary>
    public static bool HasIccProfile(ReadOnlySpan<byte> data, ImageFormat format) => format switch
    {
        ImageFormat.Png => PngChunks(data).Icc,
        ImageFormat.Jpeg => JpegHasIcc(data),
        _ => false
    };

    /// <summary>True when a PNG has an alpha channel or a tRNS chunk.</summary>
    public static bool PngHasAlpha(ReadOnlySpan<byte> png)
    {
        if (png.Length < 26)
            return false;

        var colorType = png[25];
        return colorType is 4 or 6 || PngChunks(png).Trns;
    }

    /// <summary>Bits per channel from the PNG IHDR: 1, 2, 4, 8 or 16.</summary>
    public static int PngBitDepth(ReadOnlySpan<byte> png) => png.Length < 25 ? 8 : png[24];

    // Ancillary chunks that matter here all come before the first IDAT.
    private static (bool Icc, bool Trns) PngChunks(ReadOnlySpan<byte> png)
    {
        bool icc = false, trns = false;
        var offset = 8;
        while (offset + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            var type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
                break;

            icc |= type.SequenceEqual("iCCP"u8);
            trns |= type.SequenceEqual("tRNS"u8);

            if (length > int.MaxValue - 12 - offset)
                break;

            offset += 12 + (int)length;
        }

        return (icc, trns);
    }

    // Sample precision from the start-of-frame segment: 8, or 12 for the rare 12-bit JPEG.
    private static int JpegPrecision(ReadOnlySpan<byte> jpeg)
    {
        var i = 2;
        while (i + 4 <= jpeg.Length && jpeg[i] == 0xFF)
        {
            var marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0xDA or 0xD9)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(i + 2)..]);
            if (length < 2 || i + 2 + length > jpeg.Length)
                break;

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && length > 2)
                return jpeg[i + 4];

            i += 2 + length;
        }

        return 8;
    }

    private static bool JpegHasIcc(ReadOnlySpan<byte> jpeg)
    {
        var i = 2;
        while (i + 4 <= jpeg.Length && jpeg[i] == 0xFF)
        {
            var marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0xDA or 0xD9)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(i + 2)..]);
            if (length < 2 || i + 2 + length > jpeg.Length)
                break;

            if (marker == 0xE2 && jpeg.Slice(i + 4, length - 2).StartsWith("ICC_PROFILE\0"u8))
                return true;

            i += 2 + length;
        }

        return false;
    }
}

/// <summary>WebP RIFF container: dimensions from the VP8, VP8L or VP8X chunk, orientation from an EXIF chunk.</summary>
internal static class WebPHeader
{
    private const int AnimationFlag = 0x02;
    private const int AlphaFlag = 0x10;
    private const int IccFlag = 0x20;

    /// <summary>WebP is always 8 bits per channel. Alpha and ICC come from the VP8X flags, or the VP8L alpha hint.</summary>
    public static ImageDetails ReadDetails(ReadOnlySpan<byte> data)
    {
        bool alpha = false, icc = false;
        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            var type = data.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > (uint)(data.Length - offset - 8))
                break;

            var payload = data.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("VP8X"u8) && payload.Length >= 1)
            {
                alpha |= (payload[0] & AlphaFlag) != 0;
                icc |= (payload[0] & IccFlag) != 0;
            }
            else if (type.SequenceEqual("VP8L"u8) && payload.Length >= 5 && payload[0] == 0x2F)
            {
                alpha |= (BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]) >> 28 & 1) != 0;
            }
            else if (type.SequenceEqual("ICCP"u8))
            {
                icc = true;
            }

            offset += 8 + (int)length + (int)(length & 1);
        }

        return new(8, alpha, icc);
    }

    public static ImageInfo Read(ReadOnlySpan<byte> data)
    {
        int width = 0, height = 0, orientation = 1;
        var animated = false;

        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            var type = data.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > (uint)(data.Length - offset - 8))
                break;

            var payload = data.Slice(offset + 8, (int)length);

            if (type.SequenceEqual("VP8X"u8) && payload.Length >= 10)
            {
                animated = (payload[0] & AnimationFlag) != 0;
                width = ReadUInt24(payload[4..]) + 1;
                height = ReadUInt24(payload[7..]) + 1;
            }
            else if (type.SequenceEqual("VP8 "u8) && width == 0 && payload.Length >= 10
                     && payload[3] == 0x9D && payload[4] == 0x01 && payload[5] == 0x2A)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3FFF;
            }
            else if (type.SequenceEqual("VP8L"u8) && width == 0 && payload.Length >= 5 && payload[0] == 0x2F)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
            }
            else if (type.SequenceEqual("EXIF"u8))
            {
                // Some writers keep the JPEG APP1 "Exif\0\0" prefix; the specification says the chunk starts at the TIFF header.
                var tiff = payload.StartsWith("Exif\0\0"u8) ? payload[6..] : payload;
                orientation = ExifOrientation.ReadTiff(tiff);
            }

            // Chunks are padded to an even length.
            offset += 8 + (int)length + (int)(length & 1);
        }

        return new(width, height, orientation, animated);
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | data[1] << 8 | data[2] << 16;
}

/// <summary>
/// AVIF (ISO BMFF/HEIF): reads the primary item's properties: ispe (dimensions), irot (rotation), pixi (bit depth)
/// and colr (ICC profile). An auxC alpha property anywhere means the image has alpha. Image sequences (the "avis"
/// major brand) are reported as animated.
/// </summary>
internal static class AvifHeader
{
    private readonly record struct Box(string Type, int Start, int Length);

    private readonly record struct Properties(
        int Width, int Height, int Rotation, int BitDepth, bool Alpha, bool Icc, bool Animated);

    public static ImageInfo Read(ReadOnlySpan<byte> data)
    {
        var p = ReadProperties(data);
        // Odd quarter turns display width and height swapped, like EXIF orientations 5-8.
        return new(p.Width, p.Height, p.Rotation % 2 == 1 ? 6 : 1, p.Animated);
    }

    public static ImageDetails ReadDetails(ReadOnlySpan<byte> data)
    {
        var p = ReadProperties(data);
        return new(p.BitDepth, p.Alpha, p.Icc);
    }

    private static Properties ReadProperties(ReadOnlySpan<byte> data)
    {
        var animated = data.Length >= 12 && data[8..12].SequenceEqual("avis"u8);
        var empty = new Properties(0, 0, 0, 8, false, false, animated);

        // meta is a full box: 4 bytes of version and flags before its children.
        if (Find(ListBoxes(data, 0, data.Length), "meta") is not { Length: >= 4 } meta)
            return empty;

        var children = ListBoxes(data, meta.Start + 4, meta.Length - 4);
        if (Find(children, "iprp") is not { } iprp)
            return empty;

        var iprpChildren = ListBoxes(data, iprp.Start, iprp.Length);
        if (Find(iprpChildren, "ipco") is not { } ipco)
            return empty;

        var properties = ListBoxes(data, ipco.Start, ipco.Length);
        var primary = Find(children, "pitm") is { } pitm ? ReadPrimaryItem(data.Slice(pitm.Start, pitm.Length)) : -1;
        var associated = primary >= 0 && Find(iprpChildren, "ipma") is { } ipma
            ? ReadAssociations(data.Slice(ipma.Start, ipma.Length), primary)
            : null;

        int width = 0, height = 0, rotation = 0, bitDepth = 8;
        bool alpha = false, icc = false;
        for (var index = 0; index < properties.Count; index++)
        {
            var (type, start, length) = properties[index];
            var body = data.Slice(start, length);

            // The alpha plane is its own item, so its auxC isn't associated with the primary item.
            if (type == "auxC" && body.Length > 4 && body[4..].StartsWith("urn:mpeg:mpegB:cicp:systems:auxiliary:alpha"u8))
                alpha = true;

            // ipma indices are 1-based; without associations, take the largest ispe (the primary image or grid).
            if (associated is not null && !associated.Contains(index + 1))
                continue;

            switch (type)
            {
                case "ispe" when body.Length >= 12:
                    var w = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[4..]), int.MaxValue);
                    var h = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[8..]), int.MaxValue);
                    if ((long)w * h > (long)width * height)
                        (width, height) = (w, h);
                    break;
                case "irot" when body.Length >= 1:
                    rotation = body[0] & 0x03;
                    break;
                case "pixi" when body.Length >= 6 && body[4] > 0:
                    bitDepth = body[5];
                    break;
                case "colr" when body.Length >= 4:
                    icc |= body[..4].SequenceEqual("prof"u8) || body[..4].SequenceEqual("rICC"u8);
                    break;
            }
        }

        return new(width, height, rotation, bitDepth, alpha, icc, animated);
    }

    private static int ReadPrimaryItem(ReadOnlySpan<byte> pitm)
    {
        if (pitm.Length < 6)
            return -1;

        return pitm[0] == 0
            ? BinaryPrimitives.ReadUInt16BigEndian(pitm[4..])
            : pitm.Length >= 8 ? (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(pitm[4..]), int.MaxValue) : -1;
    }

    // ipma: entry_count, then per item: item_ID, association count, and 7- or 15-bit property indices.
    private static HashSet<int>? ReadAssociations(ReadOnlySpan<byte> ipma, int itemId)
    {
        if (ipma.Length < 8)
            return null;

        var version = ipma[0];
        var wideIndices = (ipma[3] & 1) != 0;
        var count = BinaryPrimitives.ReadUInt32BigEndian(ipma[4..]);
        var offset = 8;

        for (var entry = 0u; entry < count; entry++)
        {
            var idSize = version < 1 ? 2 : 4;
            if (offset + idSize + 1 > ipma.Length)
                return null;

            var id = version < 1
                ? BinaryPrimitives.ReadUInt16BigEndian(ipma[offset..])
                : (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(ipma[offset..]), int.MaxValue);
            offset += idSize;
            int associations = ipma[offset++];

            var indexSize = wideIndices ? 2 : 1;
            if (offset + associations * indexSize > ipma.Length)
                return null;

            if (id == itemId)
            {
                var indices = new HashSet<int>();
                for (var a = 0; a < associations; a++)
                {
                    var index = wideIndices
                        ? BinaryPrimitives.ReadUInt16BigEndian(ipma[(offset + a * 2)..]) & 0x7FFF
                        : ipma[offset + a] & 0x7F;
                    indices.Add(index);
                }

                return indices;
            }

            offset += associations * indexSize;
        }

        return null;
    }

    private static Box? Find(List<Box> boxes, string type)
    {
        foreach (var box in boxes)
        {
            if (box.Type == type)
                return box;
        }

        return null;
    }

    // Box header: 32-bit size (1 = 64-bit size follows, 0 = to the end), then a 4-character type.
    // Returns each box's body as an offset and length into the whole file.
    private static List<Box> ListBoxes(ReadOnlySpan<byte> data, int start, int length)
    {
        var boxes = new List<Box>();
        var end = start + length;
        var offset = start;
        while (offset + 8 <= end && boxes.Count < 1024)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            var type = System.Text.Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
            var header = 8;

            if (size == 1)
            {
                if (offset + 16 > end)
                    break;
                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]), long.MaxValue);
                header = 16;
            }
            else if (size == 0)
            {
                size = end - offset;
            }

            if (size < header || size > end - offset)
                break;

            boxes.Add(new Box(type, offset + header, (int)size - header));
            offset += (int)size;
        }

        return boxes;
    }
}

public static class ExifOrientation
{
    /// <summary>Reads tag 0x0112 from an EXIF TIFF block. Returns 1 (normal) when absent or unreadable.</summary>
    public static int ReadTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 1;

        bool littleEndian;
        if (tiff.StartsWith("II"u8))
            littleEndian = true;
        else if (tiff.StartsWith("MM"u8))
            littleEndian = false;
        else
            return 1;

        var ifd = (int)ReadUInt32(tiff, 4, littleEndian);
        if (ifd < 8 || ifd + 2 > tiff.Length)
            return 1;

        var count = ReadUInt16(tiff, ifd, littleEndian);
        for (var n = 0; n < count; n++)
        {
            var entry = ifd + 2 + n * 12;
            if (entry + 12 > tiff.Length)
                break;

            if (ReadUInt16(tiff, entry, littleEndian) == 0x0112)
            {
                var value = ReadUInt16(tiff, entry + 8, littleEndian);
                return value is >= 1 and <= 8 ? value : 1;
            }
        }

        return 1;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..])
            : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..])
            : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
}
