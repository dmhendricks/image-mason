using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class ImageFormatTests
{
    [Fact]
    public void Detect_RecognizesEachFormatByMagicBytes()
    {
        Assert.Equal(ImageFormat.Png, ImageFormatDetector.Detect(TestImages.NoisyPng(4, 4)));
        Assert.Equal(ImageFormat.Jpeg, ImageFormatDetector.Detect(TestImages.JpegHeader(4, 4)));
        Assert.Equal(ImageFormat.WebP, ImageFormatDetector.Detect(TestImages.WebPLossy(4, 4)));
        Assert.Equal(ImageFormat.Avif, ImageFormatDetector.Detect(TestImages.AvifHeader(4, 4)));
        Assert.Equal(ImageFormat.Avif, ImageFormatDetector.Detect(TestImages.AvifHeader(4, 4, brand: "avis")));
        Assert.Equal(ImageFormat.Jxl, ImageFormatDetector.Detect(TestImages.JxlCodestream(4, 4)));
        Assert.Equal(ImageFormat.Jxl, ImageFormatDetector.Detect(TestImages.JxlContainer(TestImages.JxlCodestream(4, 4))));
        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.Detect(TestImages.AvifHeader(4, 4, brand: "heic")[..16]));
        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.Detect("GIF89a"u8));
        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.Detect("RIFF\0\0\0\0WAVE"u8));
        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.Detect([]));
    }

    [Theory]
    [InlineData("jpeg", ImageFormat.Jpeg)]
    [InlineData("JPG", ImageFormat.Jpeg)]
    [InlineData("png", ImageFormat.Png)]
    [InlineData(" webp ", ImageFormat.WebP)]
    [InlineData("avif", ImageFormat.Avif)]
    [InlineData("jxl", ImageFormat.Jxl)]
    [InlineData("JPEGXL", ImageFormat.Jxl)]
    public void TryParse_AcceptsFormatNames(string value, ImageFormat expected)
    {
        Assert.True(ImageFormatDetector.TryParse(value, out var format));
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gif")]
    [InlineData("image/png")]
    public void TryParse_RejectsOtherValues(string? value)
    {
        Assert.False(ImageFormatDetector.TryParse(value, out _));
    }

    [Fact]
    public void Extension_AndFromExtension_RoundTrip()
    {
        foreach (var format in ImageFormatDetector.Supported)
            Assert.Equal(format, ImageFormatDetector.FromExtension(format.Extension()));

        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.FromExtension(".jpeg"));
    }

    [Fact]
    public void ReadWebP_ReadsEachBitstreamHeader()
    {
        Assert.Equal(new ImageInfo(640, 427, 1), ImageInfo.Read(TestImages.WebPLossy(640, 427), ImageFormat.WebP));
        Assert.Equal(new ImageInfo(480, 320, 1), ImageInfo.Read(TestImages.WebPLossless(480, 320), ImageFormat.WebP));
        Assert.Equal(new ImageInfo(5000, 3000, 1), ImageInfo.Read(TestImages.WebPExtended(5000, 3000), ImageFormat.WebP));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadWebP_ReadsTheExifOrientation(bool exifPrefix)
    {
        var info = ImageInfo.Read(TestImages.WebPExtended(400, 300, orientation: 6, exifPrefix: exifPrefix), ImageFormat.WebP);

        Assert.Equal(6, info.Orientation);
        Assert.Equal(300, info.DisplayWidth);
    }

    [Fact]
    public void ReadWebP_FlagsAnimation()
    {
        Assert.True(ImageInfo.Read(TestImages.WebPExtended(10, 10, animated: true), ImageFormat.WebP).Animated);
        Assert.False(ImageInfo.Read(TestImages.WebPExtended(10, 10), ImageFormat.WebP).Animated);
    }

    [Fact]
    public void ReadAvif_UsesThePrimaryItemsProperties()
    {
        Assert.Equal(new ImageInfo(4032, 3024, 1), ImageInfo.Read(TestImages.AvifHeader(4032, 3024), ImageFormat.Avif));
    }

    [Fact]
    public void ReadAvif_TakesTheLargestSizeWithoutAssociations()
    {
        var info = ImageInfo.Read(TestImages.AvifHeader(4032, 3024, associations: false), ImageFormat.Avif);

        Assert.Equal((4032, 3024), (info.Width, info.Height));
    }

    [Theory]
    [InlineData(0, 4032)]
    [InlineData(1, 3024)]
    [InlineData(2, 4032)]
    [InlineData(3, 3024)]
    public void ReadAvif_QuarterTurnsSwapTheDisplayWidth(int rotation, int displayWidth)
    {
        Assert.Equal(displayWidth, ImageInfo.Read(TestImages.AvifHeader(4032, 3024, rotation), ImageFormat.Avif).DisplayWidth);
    }

    [Fact]
    public void ReadAvif_FlagsImageSequences()
    {
        Assert.True(ImageInfo.Read(TestImages.AvifHeader(10, 10, brand: "avis"), ImageFormat.Avif).Animated);
    }

    [Fact]
    public void ReadAvif_ReturnsNothingForATruncatedFile()
    {
        var avif = TestImages.AvifHeader(4032, 3024);

        for (var length = 0; length < avif.Length; length += 7)
            Assert.Equal(0, ImageInfo.Read(avif.AsSpan(0, length), ImageFormat.Avif).Width);
    }

    [Fact]
    public void ReadDetails_Png()
    {
        Assert.Equal(new ImageDetails(8, false, false), ImageHeader.ReadDetails(TestImages.NoisyPng(4, 4), ImageFormat.Png));
        Assert.Equal(new ImageDetails(8, true, true),
            ImageHeader.ReadDetails(TestImages.Png(4, 4, (_, _) => (0, 0, 0, 0), alpha: true, chunks: "iCCP"), ImageFormat.Png));
    }

    [Fact]
    public void ReadDetails_Jpeg()
    {
        Assert.Equal(new ImageDetails(8, false, true), ImageHeader.ReadDetails(TestImages.JpegHeader(4, 4, icc: true), ImageFormat.Jpeg));
    }

    [Fact]
    public void ReadDetails_WebP()
    {
        Assert.Equal(new ImageDetails(8, false, false), ImageHeader.ReadDetails(TestImages.WebPLossy(4, 4), ImageFormat.WebP));
        Assert.Equal(new ImageDetails(8, true, false), ImageHeader.ReadDetails(TestImages.WebPLossless(4, 4, alpha: true), ImageFormat.WebP));
        Assert.Equal(new ImageDetails(8, true, true),
            ImageHeader.ReadDetails(TestImages.WebPExtended(4, 4, alpha: true, icc: true), ImageFormat.WebP));
    }

    [Fact]
    public void ReadDetails_Avif()
    {
        Assert.Equal(new ImageDetails(8, false, false), ImageHeader.ReadDetails(TestImages.AvifHeader(4, 4), ImageFormat.Avif));
        Assert.Equal(new ImageDetails(10, true, true),
            ImageHeader.ReadDetails(TestImages.AvifHeader(4, 4, bitDepth: 10, alpha: true, icc: true), ImageFormat.Avif));
    }

    [Fact]
    public void DisplayHeight_SwapsWithDisplayWidth()
    {
        Assert.Equal(4032, new ImageInfo(4032, 3024, 6).DisplayHeight);
        Assert.Equal(3024, new ImageInfo(4032, 3024, 1).DisplayHeight);
    }

    [Fact]
    public void PngHasAlpha_ChecksColorTypeAndTransparencyChunk()
    {
        Assert.False(ImageHeader.PngHasAlpha(TestImages.NoisyPng(4, 4)));
        Assert.True(ImageHeader.PngHasAlpha(TestImages.NoisyPng(4, 4, alpha: true)));
        Assert.True(ImageHeader.PngHasAlpha(TestImages.Png(4, 4, (_, _) => (0, 0, 0, 255), chunks: "tRNS")));
    }

    [Fact]
    public void HasIccProfile_FindsPngAndJpegProfiles()
    {
        Assert.True(ImageHeader.HasIccProfile(TestImages.Png(4, 4, (_, _) => (0, 0, 0, 255), chunks: "iCCP"), ImageFormat.Png));
        Assert.False(ImageHeader.HasIccProfile(TestImages.NoisyPng(4, 4), ImageFormat.Png));
        Assert.True(ImageHeader.HasIccProfile(TestImages.JpegHeader(4, 4, icc: true), ImageFormat.Jpeg));
        Assert.False(ImageHeader.HasIccProfile(TestImages.JpegHeader(4, 4, orientation: 6), ImageFormat.Jpeg));
    }

    [Fact]
    public void ReadPng_ReturnsHeaderDimensions()
    {
        var info = ImageInfo.Read(TestImages.NoisyPng(37, 21), ImageFormat.Png);

        Assert.Equal(new ImageInfo(37, 21, 1), info);
        Assert.Equal(777, info.PixelCount);
    }

    [Fact]
    public void ReadJpeg_ReturnsDimensionsFromStartOfFrame()
    {
        var info = ImageInfo.Read(TestImages.JpegHeader(4032, 3024), ImageFormat.Jpeg);

        Assert.Equal(new ImageInfo(4032, 3024, 1), info);
    }

    [Theory]
    [InlineData("ff01")] // TEM
    [InlineData("ffd3")] // RST3
    [InlineData("0000")] // stray bytes
    [InlineData("ff00")] // a stuffed zero
    public void ReadJpeg_SkipsWhatLibjpegSkips(string hex)
    {
        // These carry no length; reading one would take the next marker's bytes as a length and miss the frame header.
        var header = TestImages.JpegHeader(4032, 3024, orientation: 6, icc: true);
        byte[] jpeg = [.. header[..2], .. Convert.FromHexString(hex), .. header[2..]];

        Assert.Equal(new ImageInfo(4032, 3024, 6), ImageInfo.Read(jpeg, ImageFormat.Jpeg));
        Assert.True(ImageHeader.HasIccProfile(jpeg, ImageFormat.Jpeg));
    }

    [Fact]
    public void ReadPng_WithoutIhdrFirst_HasNoSize()
    {
        var png = TestImages.NoisyPng(37, 21);
        "IHDX"u8.CopyTo(png.AsSpan(12));

        Assert.Equal(default, ImageInfo.Read(png, ImageFormat.Png));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadJpeg_ReadsExifOrientationInEitherByteOrder(bool bigEndian)
    {
        var info = ImageInfo.Read(TestImages.JpegHeader(4032, 3024, orientation: 6, bigEndianExif: bigEndian), ImageFormat.Jpeg);

        Assert.Equal(6, info.Orientation);
    }

    [Theory]
    [InlineData(0x7FFFFFFEu)]
    [InlineData(0x7FFFFFFFu)]
    [InlineData(0xFFFFFFFFu)]
    public void ExifOrientation_IgnoresAnIfdOffsetPastTheEnd(uint offset)
    {
        // "II", 42, then the first IFD's offset; nothing follows, so any offset past 8 is out of range.
        byte[] tiff = [(byte)'I', (byte)'I', 42, 0, (byte)offset, (byte)(offset >> 8), (byte)(offset >> 16), (byte)(offset >> 24), 0, 0];

        Assert.Equal(1, ExifOrientation.ReadTiff(tiff));
    }

    [Theory]
    [InlineData(1, 4032)]
    [InlineData(3, 4032)]
    [InlineData(6, 3024)]
    [InlineData(8, 3024)]
    public void DisplayWidth_SwapsDimensionsForRotatedOrientations(int orientation, int expected)
    {
        Assert.Equal(expected, new ImageInfo(4032, 3024, orientation).DisplayWidth);
    }

    [Fact]
    public void ReadJpeg_IgnoresAnOutOfRangeOrientation()
    {
        var info = ImageInfo.Read(TestImages.JpegHeader(10, 10, orientation: 9), ImageFormat.Jpeg);

        Assert.Equal(1, info.Orientation);
    }

    // The first bytes of real cjxl 0.11.2 files, with jxlinfo's reading of them: a photo, the same photo
    // recompressed from a JPEG tagged as rotated, a 2-frame animation from a GIF, a 160x107 image (whose width is
    // stored as a ratio of the height) and a photo in a custom color space, which cjxl stores without an ICC profile.
    [Theory]
    [InlineData("ff0a50cd01001388020060545e07dd89746d7f35317152e1", 640, 427, 1, false, 8, false)]
    [InlineData("ff0a504d0bc404e05b001000c8d2d447b275e601e8c56afd", 640, 427, 6, false, 8, false)]
    [InlineData("ff0ac1050158130810a002000048004b188b15805de0ff00", 16, 8, 1, true, 8, true)]
    [InlineData("ff0a50c3010013880200a532b59f200000152aa38c1bbc9c", 160, 107, 1, false, 8, false)]
    [InlineData("ff0a504d9090a079cea402a61002227f9d00ae4bd0b2547f", 640, 427, 1, false, 8, false)]
    public void ReadJxl_ReadsRealEncoderHeaders(string hex, int width, int height, int orientation, bool animated,
        int bitDepth, bool alpha)
    {
        var data = Convert.FromHexString(hex.Replace(" ", ""));

        Assert.Equal(new ImageInfo(width, height, orientation, animated), ImageInfo.Read(data, ImageFormat.Jxl));
        Assert.Equal(new ImageDetails(bitDepth, alpha, false), ImageHeader.ReadDetails(data, ImageFormat.Jxl));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadJxl_FindsTheCodestreamInTheContainer(bool partial)
    {
        // A real 480x320 lossless image with alpha, 16 bits per channel, from cjxl's jxlc box.
        var codestream = Convert.FromHexString("ff0af849fc087e800408081000b40c000080ad6c0a1ec401");
        var data = TestImages.JxlContainer(codestream, partial);

        Assert.Equal(ImageFormat.Jxl, ImageFormatDetector.Detect(data));
        Assert.Equal(new ImageInfo(480, 320, 1), ImageInfo.Read(data, ImageFormat.Jxl));
        Assert.Equal(new ImageDetails(16, true, false), ImageHeader.ReadDetails(data, ImageFormat.Jxl));
    }

    [Fact]
    public void ReadJxl_SkipsThePreviewAndAnimationHeaders()
    {
        var data = TestImages.JxlCodestream(5000, 3000, orientation: 8, preview: true, animated: true);

        Assert.Equal(new ImageInfo(5000, 3000, 8, Animated: true), ImageInfo.Read(data, ImageFormat.Jxl));
        Assert.Equal(3000, ImageInfo.Read(data, ImageFormat.Jxl).DisplayWidth);
    }

    [Theory]
    [InlineData(false, false, 8)]
    [InlineData(true, false, 16)]
    [InlineData(true, true, 12)]
    public void ReadJxl_ReadsTheDetailsAfterNamedExtraChannels(bool alpha, bool icc, int bitDepth)
    {
        var data = TestImages.JxlCodestream(64, 64, bitDepth: bitDepth, alpha: alpha, icc: icc);

        Assert.Equal(new ImageInfo(64, 64, 1), ImageInfo.Read(data, ImageFormat.Jxl));
        Assert.Equal(new ImageDetails(bitDepth, alpha, icc), ImageHeader.ReadDetails(data, ImageFormat.Jxl));
    }

    [Fact]
    public void ReadJxl_ReturnsNothingForATruncatedOrForeignFile()
    {
        var data = TestImages.JxlCodestream(64, 64, alpha: true);

        Assert.Equal(default, ImageInfo.Read(data[..6], ImageFormat.Jxl));
        Assert.Equal(default, ImageInfo.Read([0xFF, 0x0A], ImageFormat.Jxl));
        Assert.Equal(default, ImageInfo.Read(TestImages.JpegHeader(4, 4), ImageFormat.Jxl));
    }

    [Fact]
    public void ReadInfo_RejectsImagesOverThePixelLimit()
    {
        var png = TestImages.NoisyPng(100, 100);

        Assert.Equal(10_000, ImageOptimizer.ReadInfo(png, ImageFormat.Png, maxPixels: 10_000).PixelCount);
        Assert.Throws<ImageTooLargeException>(() => ImageOptimizer.ReadInfo(png, ImageFormat.Png, maxPixels: 9_999));
    }

    [Fact]
    public void ReadInfo_RejectsUnreadableDimensions()
    {
        Assert.Throws<UnsupportedImageException>(() => ImageOptimizer.ReadInfo([0xFF, 0xD8, 0xFF], ImageFormat.Jpeg, maxPixels: 1_000));
    }
}
