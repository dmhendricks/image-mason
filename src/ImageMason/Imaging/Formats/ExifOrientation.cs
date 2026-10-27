using System.Buffers.Binary;

namespace ImageMason.Imaging;

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

        // Compared unsigned: an offset near uint.MaxValue or int.MaxValue must not wrap past the bounds check.
        var offset = ReadUInt32(tiff, 4, littleEndian);
        if (offset < 8 || offset > (uint)tiff.Length - 2)
            return 1;

        var ifd = (int)offset;

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
