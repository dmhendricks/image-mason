using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ImageMason.Imaging;

/// <summary>A curated subset of EXIF: who made the image, with what, when and where. Missing fields are null.</summary>
public sealed record ExifSummary(
    string? Make,
    string? Model,
    string? LensModel,
    string? Software,
    string? DateTaken,
    string? Artist,
    string? Copyright,
    int? Iso,
    double? FNumber,
    string? ExposureTime,
    double? FocalLength,
    GpsLocation? Gps);

/// <param name="Latitude">Decimal degrees, negative south of the equator.</param>
/// <param name="Longitude">Decimal degrees, negative west of Greenwich.</param>
/// <param name="Altitude">Meters, negative below sea level.</param>
public sealed record GpsLocation(double Latitude, double Longitude, double? Altitude);

/// <summary>
/// Reads <see cref="ExifSummary"/> from the EXIF block of a JPEG (APP1), PNG (eXIf) or WebP (EXIF chunk).
/// AVIF keeps EXIF in a separate item and isn't read yet.
/// </summary>
public static class ExifReader
{
    private const int MaxStringLength = 256;

    // IFD0
    private const ushort MakeTag = 0x010F;
    private const ushort ModelTag = 0x0110;
    private const ushort SoftwareTag = 0x0131;
    private const ushort ArtistTag = 0x013B;
    private const ushort CopyrightTag = 0x8298;
    private const ushort ExifIfdTag = 0x8769;
    private const ushort GpsIfdTag = 0x8825;

    // Exif IFD
    private const ushort ExposureTimeTag = 0x829A;
    private const ushort FNumberTag = 0x829D;
    private const ushort IsoTag = 0x8827;
    private const ushort DateTimeOriginalTag = 0x9003;
    private const ushort OffsetTimeOriginalTag = 0x9011;
    private const ushort FocalLengthTag = 0x920A;
    private const ushort LensModelTag = 0xA434;

    // GPS IFD
    private const ushort LatitudeRefTag = 0x0001;
    private const ushort LatitudeTag = 0x0002;
    private const ushort LongitudeRefTag = 0x0003;
    private const ushort LongitudeTag = 0x0004;
    private const ushort AltitudeRefTag = 0x0005;
    private const ushort AltitudeTag = 0x0006;

    /// <summary>The summary, or null when the image has no EXIF or none of the summarized fields.</summary>
    public static ExifSummary? Read(ReadOnlySpan<byte> data, ImageFormat format)
    {
        var tiff = FindTiff(data, format);
        return tiff.IsEmpty ? null : ReadTiff(tiff);
    }

    public static ExifSummary? ReadTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return null;

        bool littleEndian;
        if (tiff.StartsWith("II"u8))
            littleEndian = true;
        else if (tiff.StartsWith("MM"u8))
            littleEndian = false;
        else
            return null;

        var reader = new TiffReader(tiff, littleEndian);
        var ifd0 = reader.ReadIfd(reader.UInt32(4));

        var exif = ifd0.TryGetValue(ExifIfdTag, out var exifPointer) && reader.Offset(exifPointer) is { } exifOffset
            ? reader.ReadIfd(exifOffset)
            : [];
        var gps = ifd0.TryGetValue(GpsIfdTag, out var gpsPointer) && reader.Offset(gpsPointer) is { } gpsOffset
            ? reader.ReadIfd(gpsOffset)
            : [];

        var summary = new ExifSummary(
            Make: reader.Text(ifd0, MakeTag),
            Model: reader.Text(ifd0, ModelTag),
            LensModel: reader.Text(exif, LensModelTag),
            Software: reader.Text(ifd0, SoftwareTag),
            DateTaken: DateTaken(reader.Text(exif, DateTimeOriginalTag), reader.Text(exif, OffsetTimeOriginalTag)),
            Artist: reader.Text(ifd0, ArtistTag),
            Copyright: reader.Text(ifd0, CopyrightTag),
            Iso: reader.Integer(exif, IsoTag),
            FNumber: Round(reader.Rational(exif, FNumberTag, 0), 1),
            ExposureTime: ExposureTime(reader.Rational(exif, ExposureTimeTag, 0)),
            // Two decimals: phone lenses are specified that way (1.95 mm), and one decimal would round them ambiguously.
            FocalLength: Round(reader.Rational(exif, FocalLengthTag, 0), 2),
            Gps: Location(reader, gps));

        return summary == Empty ? null : summary;
    }

    private static readonly ExifSummary Empty = new(null, null, null, null, null, null, null, null, null, null, null, null);

    private static ReadOnlySpan<byte> FindTiff(ReadOnlySpan<byte> data, ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => JpegExif(data),
        ImageFormat.Png => PngExif(data),
        ImageFormat.WebP => WebPExif(data),
        _ => default
    };

    private static ReadOnlySpan<byte> JpegExif(ReadOnlySpan<byte> jpeg)
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

            var segment = jpeg.Slice(i + 4, length - 2);
            if (marker == 0xE1 && segment.StartsWith("Exif\0\0"u8))
                return segment[6..];

            i += 2 + length;
        }

        return default;
    }

    // eXIf holds a TIFF block. Older writers put it after the image data, so every chunk is checked.
    private static ReadOnlySpan<byte> PngExif(ReadOnlySpan<byte> png)
    {
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            if (length > (uint)(png.Length - offset - 12))
                break;

            var type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual("eXIf"u8))
                return png.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("IEND"u8))
                break;

            offset += 12 + (int)length;
        }

        return default;
    }

    private static ReadOnlySpan<byte> WebPExif(ReadOnlySpan<byte> webp)
    {
        var offset = 12;
        while (offset + 8 <= webp.Length)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(webp[(offset + 4)..]);
            if (length > (uint)(webp.Length - offset - 8))
                break;

            if (webp.Slice(offset, 4).SequenceEqual("EXIF"u8))
            {
                var payload = webp.Slice(offset + 8, (int)length);
                return payload.StartsWith("Exif\0\0"u8) ? payload[6..] : payload;
            }

            offset += 8 + (int)length + (int)(length & 1);
        }

        return default;
    }

    // "2024:05:01 14:30:00" plus an optional "+02:00" -> "2024-05-01T14:30:00+02:00"
    private static string? DateTaken(string? value, string? offset)
    {
        if (value is null || !DateTime.TryParseExact(value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var taken))
            return null;

        var text = taken.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        return offset is { Length: 6 } && offset[0] is '+' or '-' && offset[3] == ':' ? text + offset : text;
    }

    // Shutter speeds under a second read as photographers write them: 1/250.
    private static string? ExposureTime(double? seconds) => seconds switch
    {
        null or <= 0 => null,
        < 1 => "1/" + Math.Round(1 / seconds.Value).ToString(CultureInfo.InvariantCulture),
        _ => Math.Round(seconds.Value, 1).ToString(CultureInfo.InvariantCulture)
    };

    private static GpsLocation? Location(TiffReader reader, Dictionary<ushort, Entry> gps)
    {
        var latitude = Degrees(reader, gps, LatitudeTag);
        var longitude = Degrees(reader, gps, LongitudeTag);
        if (latitude is null || longitude is null || Math.Abs(latitude.Value) > 90 || Math.Abs(longitude.Value) > 180)
            return null;

        if (reader.Text(gps, LatitudeRefTag) == "S")
            latitude = -latitude;
        if (reader.Text(gps, LongitudeRefTag) == "W")
            longitude = -longitude;

        var altitude = reader.Rational(gps, AltitudeTag, 0);
        if (altitude is not null && reader.Integer(gps, AltitudeRefTag) == 1)
            altitude = -altitude;

        return new GpsLocation(Math.Round(latitude.Value, 6), Math.Round(longitude.Value, 6), Round(altitude, 1));
    }

    // Degrees, minutes and seconds as three rationals.
    private static double? Degrees(TiffReader reader, Dictionary<ushort, Entry> ifd, ushort tag)
    {
        var degrees = reader.Rational(ifd, tag, 0);
        var minutes = reader.Rational(ifd, tag, 1);
        var seconds = reader.Rational(ifd, tag, 2);
        return degrees is null ? null : degrees + (minutes ?? 0) / 60 + (seconds ?? 0) / 3600;
    }

    private static double? Round(double? value, int digits) => value is { } v ? Math.Round(v, digits) : null;

    private readonly record struct Entry(ushort Type, uint Count, int ValueOffset);

    private readonly ref struct TiffReader(ReadOnlySpan<byte> tiff, bool littleEndian)
    {
        private readonly ReadOnlySpan<byte> _tiff = tiff;

        public uint UInt32(int offset) =>
            offset + 4 > _tiff.Length ? 0
            : littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(_tiff[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(_tiff[offset..]);

        private ushort UInt16(int offset) =>
            offset + 2 > _tiff.Length ? (ushort)0
            : littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(_tiff[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(_tiff[offset..]);

        /// <summary>Entries of the IFD at <paramref name="offset"/>, by tag. Values are not read until asked for.</summary>
        public Dictionary<ushort, Entry> ReadIfd(uint offset)
        {
            var entries = new Dictionary<ushort, Entry>();
            if (offset < 8 || offset > (uint)_tiff.Length - 2)
                return entries;

            var start = (int)offset;
            var count = UInt16(start);
            for (var n = 0; n < count; n++)
            {
                var entry = start + 2 + n * 12;
                if (entry + 12 > _tiff.Length)
                    break;

                var type = UInt16(entry + 2);
                var valueCount = UInt32(entry + 4);
                var size = TypeSize(type) * (long)valueCount;
                if (size == 0)
                    continue;

                // Values of 4 bytes or less are stored in the entry itself; larger ones at an offset.
                var valueOffset = size <= 4 ? entry + 8 : (long)UInt32(entry + 8);
                if (valueOffset + size > _tiff.Length)
                    continue;

                entries.TryAdd(UInt16(entry), new Entry(type, valueCount, (int)valueOffset));
            }

            return entries;
        }

        /// <summary>The IFD offset a pointer entry (LONG) holds.</summary>
        public uint? Offset(Entry entry) => entry.Type is 4 or 13 ? UInt32(entry.ValueOffset) : null;

        public string? Text(Dictionary<ushort, Entry> ifd, ushort tag)
        {
            if (!ifd.TryGetValue(tag, out var entry) || entry.Type != 2)
                return null;

            var bytes = _tiff.Slice(entry.ValueOffset, (int)Math.Min(entry.Count, MaxStringLength));
            var end = bytes.IndexOf((byte)0);
            var text = Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes).Trim();
            return text.Length == 0 || text.Any(char.IsControl) ? null : text;
        }

        public int? Integer(Dictionary<ushort, Entry> ifd, ushort tag)
        {
            if (!ifd.TryGetValue(tag, out var entry))
                return null;

            return entry.Type switch
            {
                1 or 7 => _tiff[entry.ValueOffset],
                3 => UInt16(entry.ValueOffset),
                4 => (int)Math.Min(UInt32(entry.ValueOffset), int.MaxValue),
                _ => null
            };
        }

        /// <summary>The <paramref name="index"/>th value of a RATIONAL or SRATIONAL entry.</summary>
        public double? Rational(Dictionary<ushort, Entry> ifd, ushort tag, int index)
        {
            if (!ifd.TryGetValue(tag, out var entry) || entry.Type is not (5 or 10) || index >= entry.Count)
                return null;

            var offset = entry.ValueOffset + index * 8;
            var numerator = UInt32(offset);
            var denominator = UInt32(offset + 4);
            if (denominator == 0)
                return null;

            return entry.Type == 10 ? (double)(int)numerator / (int)denominator : (double)numerator / denominator;
        }

        private static int TypeSize(ushort type) => type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,
            4 or 9 or 11 or 13 => 4,
            5 or 10 or 12 => 8,
            _ => 0
        };
    }
}
