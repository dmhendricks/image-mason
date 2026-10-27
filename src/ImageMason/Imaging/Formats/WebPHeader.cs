using System.Buffers.Binary;

namespace ImageMason.Imaging;

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
        var chunks = new RiffChunks(data);
        while (chunks.MoveNext())
        {
            var type = chunks.Type;
            var payload = chunks.Data;
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
        }

        return new(8, alpha, icc);
    }

    public static ImageInfo Read(ReadOnlySpan<byte> data)
    {
        int width = 0, height = 0, orientation = 1;
        var animated = false;

        var chunks = new RiffChunks(data);
        while (chunks.MoveNext())
        {
            var type = chunks.Type;
            var payload = chunks.Data;
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
        }

        return new(width, height, orientation, animated);
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | data[1] << 8 | data[2] << 16;
}
