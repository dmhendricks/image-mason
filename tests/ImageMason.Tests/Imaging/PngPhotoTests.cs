using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class PngPhotoTests
{
    private const long NoLimit = long.MaxValue;

    private static bool IsPhoto(byte[] png, long maxPixels = NoLimit) =>
        PngPhoto.IsPhoto(png, maxPixels, TestContext.Current.CancellationToken);

    [Fact]
    public void ManyColorsWithoutTransparency_IsPhoto()
    {
        Assert.True(IsPhoto(TestImages.NoisyPng(256, 256)));
    }

    [Fact]
    public void OpaqueAlphaChannel_StillCountsAsPhoto()
    {
        Assert.True(IsPhoto(TestImages.NoisyPng(256, 256, alpha: true, alphaValue: 255)));
    }

    [Fact]
    public void AnyTransparency_IsNotPhoto()
    {
        Assert.False(IsPhoto(TestImages.NoisyPng(256, 256, alpha: true, alphaValue: 200)));
    }

    [Fact]
    public void FewColors_IsNotPhoto()
    {
        var graphic = TestImages.Png(256, 256, (x, _) => x < 128 ? ((byte)200, (byte)60, (byte)40, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255));

        Assert.False(IsPhoto(graphic));
    }

    [Fact]
    public void NonPngInput_IsNotPhoto()
    {
        Assert.False(IsPhoto(TestImages.JpegHeader(10, 10)));
        Assert.False(IsPhoto([]));
    }

    [Fact]
    public void OverThePixelLimit_IsNotDecoded()
    {
        // 65,536 pixels against a limit of 65,535: rejected from the header, before any row is inflated.
        Assert.False(IsPhoto(TestImages.NoisyPng(256, 256), maxPixels: 256 * 256 - 1));
        Assert.True(IsPhoto(TestImages.NoisyPng(256, 256), maxPixels: 256 * 256));
    }

    [Fact]
    public void Cancellation_StopsTheRowScan()
    {
        // Opaque RGBA has to be read to the last row (any pixel could be transparent), so this reaches the scan.
        var png = TestImages.NoisyPng(256, 256, alpha: true, alphaValue: 255);

        Assert.Throws<OperationCanceledException>(() => PngPhoto.IsPhoto(png, NoLimit, new CancellationToken(canceled: true)));
    }
}
