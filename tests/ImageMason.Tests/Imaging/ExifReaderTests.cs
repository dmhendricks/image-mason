using ImageMason.Imaging;
using static ImageMason.Tests.TestImages;

namespace ImageMason.Tests.Imaging;

public sealed class ExifReaderTests
{
    // A phone photo: camera, lens, exposure and a location in Pittsburgh (40°26'46.3"N 79°58'56.5"W).
    private static byte[] PhoneExif(bool bigEndian = false, string latitudeRef = "N", string longitudeRef = "W", ushort altitudeRef = 0) =>
        Tiff(bigEndian,
            ifd0:
            [
                new Tag(0x010F, "Apple"),
                new Tag(0x0110, "iPhone 15 Pro"),
                new Tag(0x0131, "17.5.1"),
                new Tag(0x013B, "Jane Doe"),
                new Tag(0x8298, "(c) 2026 Jane Doe"),
            ],
            exif:
            [
                new Tag(0x829A, new uint[] { 1, 250 }),
                new Tag(0x829D, new uint[] { 178, 100 }),
                new Tag(0x8827, (ushort)400),
                new Tag(0x9003, "2026:05:01 14:30:00"),
                new Tag(0x9011, "-04:00"),
                new Tag(0x920A, new uint[] { 1950, 1000 }),
                new Tag(0xA434, "iPhone 15 Pro back camera 6.765mm f/1.78"),
            ],
            gps:
            [
                new Tag(0x0001, latitudeRef),
                new Tag(0x0002, new uint[] { 40, 1, 26, 1, 463, 10 }),
                new Tag(0x0003, longitudeRef),
                new Tag(0x0004, new uint[] { 79, 1, 58, 1, 565, 10 }),
                new Tag(0x0005, altitudeRef),
                new Tag(0x0006, new uint[] { 2735, 10 }),
            ]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadTiff_ReadsEverySummarizedField(bool bigEndian)
    {
        var exif = ExifReader.ReadTiff(PhoneExif(bigEndian));

        Assert.NotNull(exif);
        Assert.Equal("Apple", exif.Make);
        Assert.Equal("iPhone 15 Pro", exif.Model);
        Assert.Equal("iPhone 15 Pro back camera 6.765mm f/1.78", exif.LensModel);
        Assert.Equal("17.5.1", exif.Software);
        Assert.Equal("2026-05-01T14:30:00-04:00", exif.DateTaken);
        Assert.Equal("Jane Doe", exif.Artist);
        Assert.Equal("(c) 2026 Jane Doe", exif.Copyright);
        Assert.Equal(400, exif.Iso);
        Assert.Equal(1.8, exif.FNumber);
        Assert.Equal("1/250", exif.ExposureTime);
        Assert.Equal(1.95, exif.FocalLength);
        Assert.Equal(new GpsLocation(40.446194, -79.982361, 273.5), exif.Gps);
    }

    [Fact]
    public void ReadTiff_SignsTheSouthernAndWesternHemispheresAndBelowSeaLevel()
    {
        var gps = ExifReader.ReadTiff(PhoneExif(latitudeRef: "S", longitudeRef: "E", altitudeRef: 1))!.Gps;

        Assert.Equal(new GpsLocation(-40.446194, 79.982361, -273.5), gps);
    }

    [Fact]
    public void ReadTiff_WritesLongExposuresInSeconds()
    {
        var exif = ExifReader.ReadTiff(Tiff(false, ifd0: [new Tag(0x010F, "Canon")], exif: [new Tag(0x829A, new uint[] { 5, 2 })]));

        Assert.Equal("2.5", exif!.ExposureTime);
    }

    [Fact]
    public void ReadTiff_ReturnsNullWithoutSummarizedFields()
    {
        // Only an orientation tag, as many edited images have.
        var tiff = Tiff(false, ifd0: [new Tag(0x0112, (ushort)6)]);

        Assert.Null(ExifReader.ReadTiff(tiff));
        Assert.Null(ExifReader.ReadTiff([]));
        Assert.Null(ExifReader.ReadTiff("XX*\0\0\0\0\0"u8));
    }

    [Fact]
    public void ReadTiff_IgnoresBlankTextAndAMissingGpsCoordinate()
    {
        var exif = ExifReader.ReadTiff(Tiff(false,
            ifd0: [new Tag(0x010F, "   "), new Tag(0x0110, "Pixel 9")],
            gps: [new Tag(0x0001, "N"), new Tag(0x0002, new uint[] { 40, 1, 0, 1, 0, 1 })]));

        Assert.Null(exif!.Make);
        Assert.Equal("Pixel 9", exif.Model);
        Assert.Null(exif.Gps);
    }

    [Fact]
    public void ReadTiff_SurvivesTruncation()
    {
        var tiff = PhoneExif();

        for (var length = 0; length < tiff.Length; length++)
            ExifReader.ReadTiff(tiff.AsSpan(0, length));
    }

    [Fact]
    public void Read_FindsExifInEachContainer()
    {
        var tiff = PhoneExif();

        Assert.Equal("Apple", ExifReader.Read(JpegHeader(4, 4, exif: tiff), ImageFormat.Jpeg)?.Make);
        Assert.Equal("Apple", ExifReader.Read(WithPngChunk(NoisyPng(4, 4), "eXIf", tiff), ImageFormat.Png)?.Make);
        Assert.Equal("Apple", ExifReader.Read(WebPExtended(4, 4, exif: tiff), ImageFormat.WebP)?.Make);
        Assert.Equal("Apple", ExifReader.Read(WebPExtended(4, 4, exif: tiff, exifPrefix: true), ImageFormat.WebP)?.Make);
        Assert.Null(ExifReader.Read(NoisyPng(4, 4), ImageFormat.Png));
        Assert.Null(ExifReader.Read(AvifHeader(4, 4), ImageFormat.Avif));
    }
}
