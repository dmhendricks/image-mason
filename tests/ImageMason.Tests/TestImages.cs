using System.Buffers.Binary;
using System.IO.Compression;

namespace ImageMason.Tests;

/// <summary>Builds small PNG, JPEG, WebP and AVIF byte streams in memory, so the tests need no fixture files.</summary>
internal static class TestImages
{
    /// <summary>An 8-bit RGB (colorType 2) or RGBA (colorType 6) PNG whose pixels come from <paramref name="pixel"/>.</summary>
    /// <param name="chunks">Ancillary chunks written before IDAT, such as "iCCP" or "tRNS".</param>
    public static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel, bool alpha = false,
        params string[] chunks)
    {
        var channels = alpha ? 4 : 3;
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: none
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                raw.WriteByte(r);
                raw.WriteByte(g);
                raw.WriteByte(b);
                if (alpha)
                    raw.WriteByte(a);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw.ToArray());

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; // bit depth
        header[9] = (byte)(channels == 4 ? 6 : 2);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        foreach (var chunk in chunks)
            WriteChunk(png, chunk, [0]);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>Random colors: what a photo looks like to the color counter.</summary>
    public static byte[] NoisyPng(int width, int height, bool alpha = false, byte alphaValue = 255)
    {
        var random = new Random(42);
        return Png(width, height, (_, _) => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alphaValue), alpha);
    }

    /// <summary>A JPEG header (SOI, optional EXIF APP1 with an orientation tag, SOF0, SOS) without image data.</summary>
    /// <param name="exif">A whole TIFF block for APP1, instead of one holding only the orientation.</param>
    public static byte[] JpegHeader(int width, int height, int? orientation = null, bool bigEndianExif = false, bool icc = false,
        byte[]? exif = null)
    {
        using var jpeg = new MemoryStream();
        jpeg.Write([0xFF, 0xD8]);

        if ((orientation is { } value ? ExifTiff(value, bigEndianExif) : exif) is { } tiff)
        {
            byte[] app1 = [.. "Exif\0\0"u8, .. tiff];
            WriteSegment(jpeg, 0xE1, app1);
        }

        if (icc)
            WriteSegment(jpeg, 0xE2, [.. "ICC_PROFILE\0"u8, 1, 1]);

        var sof = new byte[6];
        sof[0] = 8; // precision
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(1), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(3), (ushort)width);
        WriteSegment(jpeg, 0xC0, sof);
        WriteSegment(jpeg, 0xDA, [0]);
        return jpeg.ToArray();
    }

    /// <summary>A lossy WebP: a "VP8 " frame header only.</summary>
    public static byte[] WebPLossy(int width, int height)
    {
        var frame = new byte[10];
        frame[3] = 0x9D;
        frame[4] = 0x01;
        frame[5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8), (ushort)height);
        return Riff(("VP8 ", frame));
    }

    /// <summary>A lossless WebP: a VP8L header with 14-bit width-1 and height-1, and the alpha hint bit.</summary>
    public static byte[] WebPLossless(int width, int height, bool alpha = false)
    {
        var header = new byte[5];
        header[0] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), (uint)((width - 1) | (height - 1) << 14 | (alpha ? 1 : 0) << 28));
        return Riff(("VP8L", header));
    }

    /// <summary>An extended WebP: VP8X canvas size and flags, an optional EXIF chunk, then a VP8L image.</summary>
    public static byte[] WebPExtended(int width, int height, bool animated = false, int? orientation = null, bool exifPrefix = false,
        bool alpha = false, bool icc = false, byte[]? exif = null)
    {
        var vp8x = new byte[10];
        vp8x[0] = (byte)((animated ? 0x02 : 0) | (orientation is null && exif is null ? 0 : 0x08) | (alpha ? 0x10 : 0) | (icc ? 0x20 : 0));
        WriteUInt24(vp8x, 4, width - 1);
        WriteUInt24(vp8x, 7, height - 1);

        var chunks = new List<(string, byte[])> { ("VP8X", vp8x) };
        if ((orientation is { } value ? ExifTiff(value, false) : exif) is { } tiff)
            chunks.Add(("EXIF", exifPrefix ? [.. "Exif\0\0"u8, .. tiff] : tiff));
        chunks.Add(("VP8L", [0x2F, 0, 0, 0, 0]));
        return Riff([.. chunks]);
    }

    /// <summary>
    /// An AVIF header: ftyp, then meta with pitm (item 1), and iprp holding the image's ispe, irot, pixi and colr,
    /// plus a smaller ispe and an optional alpha auxC that belong to a second item.
    /// </summary>
    public static byte[] AvifHeader(int width, int height, int rotation = 0, string brand = "avif", bool associations = true,
        int bitDepth = 8, bool alpha = false, bool icc = false)
    {
        var ispe = FullBox("ispe", [.. U32(width), .. U32(height)]);
        var thumbnail = FullBox("ispe", [.. U32(width / 4), .. U32(height / 4)]);
        var irot = Box("irot", [(byte)rotation]);
        var pixi = FullBox("pixi", [3, (byte)bitDepth, (byte)bitDepth, (byte)bitDepth]);
        var colr = Box("colr", icc ? [.. "prof"u8, 0, 0] : [.. "nclx"u8, 0, 1, 0, 13, 0, 6, 0x80]);
        var auxC = FullBox("auxC", [.. "urn:mpeg:mpegB:cicp:systems:auxiliary:alpha\0"u8]);
        var ipco = Box("ipco", [.. ispe, .. thumbnail, .. irot, .. pixi, .. colr, .. (alpha ? auxC : [])]);

        // Item 1 (primary): properties 1 (ispe), 3 (irot), 4 (pixi), 5 (colr). Item 2: 2 (thumbnail ispe), 6 (auxC).
        byte[] entries = [.. U32(2), 0, 1, 4, 0x81, 0x03, 0x04, 0x85, 0, 2, 2, 0x82, 0x86];
        var ipma = FullBox("ipma", entries);
        var iprp = Box("iprp", associations ? [.. ipco, .. ipma] : ipco);

        var pitm = FullBox("pitm", [0, 1]);
        var meta = FullBox("meta", [.. Box("hdlr", new byte[20]), .. pitm, .. iprp]);
        var ftyp = Box("ftyp", [.. System.Text.Encoding.ASCII.GetBytes(brand), 0, 0, 0, 0, .. "mif1"u8, .. "miaf"u8]);
        return [.. ftyp, .. meta];
    }

    /// <summary>Inserts a chunk just before IEND, where older writers put eXIf.</summary>
    public static byte[] WithPngChunk(byte[] png, string type, byte[] data)
    {
        using var chunk = new MemoryStream();
        WriteChunk(chunk, type, data);
        var iend = png.Length - 12;
        return [.. png.AsSpan(0, iend), .. chunk.ToArray(), .. png.AsSpan(iend)];
    }

    /// <summary>A TIFF value: text (ASCII), a ushort (SHORT), a uint (LONG) or rationals as numerator/denominator pairs.</summary>
    public sealed record Tag(ushort Id, object Value);

    /// <summary>
    /// An EXIF TIFF block: IFD0, then optional Exif and GPS IFDs (with the pointer entries added to IFD0), then the
    /// values that don't fit in an entry.
    /// </summary>
    public static byte[] Tiff(bool bigEndian, Tag[] ifd0, Tag[]? exif = null, Tag[]? gps = null)
    {
        static int IfdSize(int entries) => 2 + entries * 12 + 4;

        var ifd0Count = ifd0.Length + (exif is null ? 0 : 1) + (gps is null ? 0 : 1);
        var exifOffset = 8 + IfdSize(ifd0Count);
        var gpsOffset = exifOffset + (exif is null ? 0 : IfdSize(exif.Length));
        var dataOffset = gpsOffset + (gps is null ? 0 : IfdSize(gps.Length));

        var head = new List<byte>();
        var data = new List<byte>();
        head.AddRange(bigEndian ? "MM"u8 : "II"u8);
        head.AddRange(U16(42, bigEndian));
        head.AddRange(U32(8, bigEndian));

        void WriteIfd(IEnumerable<Tag> tags)
        {
            var list = tags.ToList();
            head.AddRange(U16((ushort)list.Count, bigEndian));
            foreach (var tag in list)
            {
                var (type, count, value) = tag.Value switch
                {
                    string text => ((ushort)2, (uint)text.Length + 1, [.. System.Text.Encoding.ASCII.GetBytes(text), 0]),
                    ushort number => ((ushort)3, 1u, U16(number, bigEndian)),
                    uint number => ((ushort)4, 1u, U32(number, bigEndian)),
                    uint[] pairs => ((ushort)5, (uint)pairs.Length / 2, pairs.SelectMany(p => U32(p, bigEndian)).ToArray()),
                    _ => throw new ArgumentException($"Unsupported TIFF value {tag.Value}")
                };

                head.AddRange(U16(tag.Id, bigEndian));
                head.AddRange(U16(type, bigEndian));
                head.AddRange(U32(count, bigEndian));
                if (value.Length <= 4)
                {
                    head.AddRange(value);
                    head.AddRange(new byte[4 - value.Length]);
                }
                else
                {
                    head.AddRange(U32((uint)(dataOffset + data.Count), bigEndian));
                    data.AddRange(value);
                }
            }

            head.AddRange(U32(0, bigEndian)); // no next IFD
        }

        var pointers = new List<Tag>();
        if (exif is not null)
            pointers.Add(new Tag(0x8769, (uint)exifOffset));
        if (gps is not null)
            pointers.Add(new Tag(0x8825, (uint)gpsOffset));

        WriteIfd(ifd0.Concat(pointers).OrderBy(t => t.Id));
        if (exif is not null)
            WriteIfd(exif.OrderBy(t => t.Id));
        if (gps is not null)
            WriteIfd(gps.OrderBy(t => t.Id));

        return [.. head, .. data];
    }

    private static byte[] U16(ushort value, bool bigEndian)
    {
        var bytes = new byte[2];
        Write16(bytes, 0, value, bigEndian);
        return bytes;
    }

    private static byte[] U32(uint value, bool bigEndian)
    {
        var bytes = new byte[4];
        Write32(bytes, 0, value, bigEndian);
        return bytes;
    }

    private static byte[] Box(string type, byte[] body) =>
        [.. U32(body.Length + 8), .. System.Text.Encoding.ASCII.GetBytes(type), .. body];

    private static byte[] FullBox(string type, byte[] body) => Box(type, [0, 0, 0, 0, .. body]);

    private static byte[] U32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        return bytes;
    }

    private static byte[] Riff(params (string Type, byte[] Data)[] chunks)
    {
        using var body = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        foreach (var (type, data) in chunks)
        {
            body.Write(System.Text.Encoding.ASCII.GetBytes(type));
            BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)data.Length);
            body.Write(length);
            body.Write(data);
            if (data.Length % 2 == 1)
                body.WriteByte(0);
        }

        var riff = new byte[12];
        "RIFF"u8.CopyTo(riff);
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)(body.Length + 4));
        "WEBP"u8.CopyTo(riff.AsSpan(8));
        return [.. riff, .. body.ToArray()];
    }

    private static byte[] ExifTiff(int orientation, bool bigEndian)
    {
        var tiff = new byte[26];
        (bigEndian ? "MM"u8 : "II"u8).CopyTo(tiff);
        Write16(tiff, 2, 42, bigEndian);
        Write32(tiff, 4, 8, bigEndian);  // first IFD offset
        Write16(tiff, 8, 1, bigEndian);  // one entry
        Write16(tiff, 10, 0x0112, bigEndian); // orientation tag
        Write16(tiff, 12, 3, bigEndian); // SHORT
        Write32(tiff, 14, 1, bigEndian); // count
        Write16(tiff, 18, (ushort)orientation, bigEndian);
        return tiff;
    }

    private static void WriteUInt24(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write([0, 0, 0, 0]); // CRC: not checked by the code under test
    }

    private static void WriteSegment(Stream stream, byte marker, byte[] data)
    {
        stream.Write([0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2)]);
        stream.Write(data);
    }

    private static void Write16(byte[] buffer, int offset, ushort value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), value);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);
    }

    private static void Write32(byte[] buffer, int offset, uint value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);
    }
}
