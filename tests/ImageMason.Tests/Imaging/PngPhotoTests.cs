using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class PngPhotoTests
{
    [Fact]
    public void ManyColorsWithoutTransparency_IsPhoto()
    {
        Assert.True(PngPhoto.IsPhoto(TestImages.NoisyPng(256, 256)));
    }

    [Fact]
    public void OpaqueAlphaChannel_StillCountsAsPhoto()
    {
        Assert.True(PngPhoto.IsPhoto(TestImages.NoisyPng(256, 256, alpha: true, alphaValue: 255)));
    }

    [Fact]
    public void AnyTransparency_IsNotPhoto()
    {
        Assert.False(PngPhoto.IsPhoto(TestImages.NoisyPng(256, 256, alpha: true, alphaValue: 200)));
    }

    [Fact]
    public void FewColors_IsNotPhoto()
    {
        var graphic = TestImages.Png(256, 256, (x, _) => x < 128 ? ((byte)200, (byte)60, (byte)40, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255));

        Assert.False(PngPhoto.IsPhoto(graphic));
    }

    [Fact]
    public void NonPngInput_IsNotPhoto()
    {
        Assert.False(PngPhoto.IsPhoto(TestImages.JpegHeader(10, 10)));
        Assert.False(PngPhoto.IsPhoto([]));
    }
}
