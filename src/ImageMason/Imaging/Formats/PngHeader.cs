using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// PNG: dimensions and bit depth from IHDR, which must be the first chunk; alpha from the color type or a tRNS chunk,
/// and an ICC profile from iCCP, both before the image data. Works on the first part of a file, too.
/// </summary>
internal static class PngHeader
{
    // 8-byte signature, then IHDR's length, type, width and height.
    public static ImageInfo Read(ReadOnlySpan<byte> png) =>
        png.Length < 24 || !png[12..16].SequenceEqual("IHDR"u8)
            ? default
            : new((int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]), 1);

    public static ImageDetails ReadDetails(ReadOnlySpan<byte> png) => new(BitDepth(png), HasAlpha(png), HasIcc(png));

    /// <summary>Bits per channel from IHDR: 1, 2, 4, 8 or 16.</summary>
    public static int BitDepth(ReadOnlySpan<byte> png) => png.Length < 25 ? 8 : png[24];

    /// <summary>An alpha channel (color types 4 and 6) or a tRNS chunk.</summary>
    public static bool HasAlpha(ReadOnlySpan<byte> png) => png.Length >= 26 && (png[25] is 4 or 6 || Ancillary(png).Trns);

    public static bool HasIcc(ReadOnlySpan<byte> png) => Ancillary(png).Icc;

    // The chunks that matter here all come before the first IDAT. A chunk that's cut off still counts.
    private static (bool Icc, bool Trns) Ancillary(ReadOnlySpan<byte> png)
    {
        bool icc = false, trns = false;
        var chunks = new PngChunks(png);
        while (chunks.MoveNext())
        {
            var type = chunks.Type;
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
                break;

            icc |= type.SequenceEqual("iCCP"u8);
            trns |= type.SequenceEqual("tRNS"u8);
        }

        return (icc, trns);
    }
}
