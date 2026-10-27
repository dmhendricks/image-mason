using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// JPEG: dimensions and sample precision from the first start-of-frame segment, orientation from the EXIF APP1
/// segment, and an ICC profile from APP2. JPEG has no alpha.
/// </summary>
internal static class JpegHeader
{
    public static ImageInfo Read(ReadOnlySpan<byte> jpeg)
    {
        int width = 0, height = 0, orientation = 1;

        var segments = new JpegSegments(jpeg);
        while (segments.MoveNext())
        {
            if (segments.Marker == 0xE1 && segments.Data.StartsWith("Exif\0\0"u8))
                orientation = ExifOrientation.ReadTiff(segments.Data[6..]);

            // The first frame header; libjpeg rejects a second.
            if (segments.IsStartOfFrame && width == 0 && segments.Data.Length >= 5)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(segments.Data[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segments.Data[3..]);
            }
        }

        return new(width, height, orientation);
    }

    public static ImageDetails ReadDetails(ReadOnlySpan<byte> jpeg) => new(Precision(jpeg), false, HasIcc(jpeg));

    // Sample precision from the start-of-frame segment: 8, or 12 for the rare 12-bit JPEG.
    private static int Precision(ReadOnlySpan<byte> jpeg)
    {
        var segments = new JpegSegments(jpeg);
        while (segments.MoveNext())
        {
            if (segments.IsStartOfFrame && segments.Data.Length > 0)
                return segments.Data[0];
        }

        return 8;
    }

    public static bool HasIcc(ReadOnlySpan<byte> jpeg)
    {
        var segments = new JpegSegments(jpeg);
        while (segments.MoveNext())
        {
            if (segments.Marker == 0xE2 && segments.Data.StartsWith("ICC_PROFILE\0"u8))
                return true;
        }

        return false;
    }
}
