using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class ResizeRequestTests
{
    // Expected sizes are what vipsthumbnail produced for a 640x427 photo with the same arguments.
    [Theory]
    [InlineData(320, 320, ResizeFit.Contain, false, 320, 214)]
    [InlineData(320, null, ResizeFit.Contain, false, 320, 214)]
    [InlineData(null, 200, ResizeFit.Contain, false, 300, 200)]
    [InlineData(1000, 1000, ResizeFit.Contain, false, 640, 427)]
    [InlineData(1000, 1000, ResizeFit.Contain, true, 1000, 667)]
    [InlineData(300, 300, ResizeFit.Cover, false, 300, 300)]
    [InlineData(1000, 300, ResizeFit.Cover, false, 640, 300)]
    [InlineData(500, 500, ResizeFit.Cover, false, 500, 427)]
    [InlineData(1000, 300, ResizeFit.Cover, true, 1000, 300)]
    [InlineData(300, 300, ResizeFit.Fill, false, 300, 300)]
    [InlineData(1000, 100, ResizeFit.Fill, false, 1000, 100)]
    public void OutputSize_MatchesVipsthumbnail(int? width, int? height, ResizeFit fit, bool upscale, int expectedWidth, int expectedHeight)
    {
        var size = new ResizeRequest(width, height, fit, upscale).OutputSize(640, 427);

        Assert.Equal((expectedWidth, expectedHeight), size);
    }

    [Theory]
    [InlineData(320, null, ResizeFit.Contain, false, CropPosition.Center, "--size 320x>")]
    [InlineData(null, 200, ResizeFit.Contain, true, CropPosition.Center, "--size x200")]
    [InlineData(300, 300, ResizeFit.Cover, false, CropPosition.Center, "--size 300x300> --smartcrop centre")]
    [InlineData(300, 300, ResizeFit.Cover, true, CropPosition.Attention, "--size 300x300 --smartcrop attention")]
    [InlineData(300, 300, ResizeFit.Fill, false, CropPosition.Center, "--size 300x300!")]
    public void VipsArguments_EncodeFitAndUpscale(int? width, int? height, ResizeFit fit, bool upscale, CropPosition crop, string expected)
    {
        var arguments = new ResizeRequest(width, height, fit, upscale, crop).VipsArguments();

        Assert.Equal(expected, string.Join(' ', arguments));
    }
}
