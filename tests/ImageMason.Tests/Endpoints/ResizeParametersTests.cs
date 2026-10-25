using ImageMason.Endpoints;
using ImageMason.Imaging;

namespace ImageMason.Tests.Endpoints;

public sealed class ResizeParametersTests
{
    [Fact]
    public void TryParse_NothingSent_HasNoBox()
    {
        Assert.True(ResizeParameters.TryParse(null, null, null, null, null, out var resize, out var error));
        Assert.Null(resize);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(800, null, null, null, null, 800, null, ResizeFit.Contain, false, CropPosition.Center)]
    [InlineData(null, 600, null, null, null, null, 600, ResizeFit.Contain, false, CropPosition.Center)]
    [InlineData(800, 600, null, null, null, 800, 600, ResizeFit.Contain, false, CropPosition.Center)]
    [InlineData(800, 600, "Cover", " attention ", true, 800, 600, ResizeFit.Cover, true, CropPosition.Attention)]
    [InlineData(800, 600, "fill", null, null, 800, 600, ResizeFit.Fill, false, CropPosition.Center)]
    public void TryParse_ValidBox(int? width, int? height, string? fit, string? crop, bool? upscale,
        int? expectedWidth, int? expectedHeight, ResizeFit expectedFit, bool expectedUpscale, CropPosition expectedCrop)
    {
        Assert.True(ResizeParameters.TryParse(width, height, fit, crop, upscale, out var resize, out _));
        Assert.Equal(new ResizeRequest(expectedWidth, expectedHeight, expectedFit, expectedUpscale, expectedCrop), resize);
    }

    [Theory]
    [InlineData(0, null, null, null, null, "width and height must be between 1 and 65535.")]
    [InlineData(null, 65536, null, null, null, "width and height must be between 1 and 65535.")]
    [InlineData(null, null, "cover", null, null, "fit, crop and upscale need width, height or both.")]
    [InlineData(null, null, null, "center", null, "fit, crop and upscale need width, height or both.")]
    [InlineData(null, null, null, null, true, "fit, crop and upscale need width, height or both.")]
    [InlineData(800, 600, "stretch", null, null, "fit must be 'contain', 'cover' or 'fill'.")]
    [InlineData(800, null, "cover", null, null, "fit=cover needs both width and height.")]
    [InlineData(null, 600, "fill", null, null, "fit=fill needs both width and height.")]
    [InlineData(800, 600, "cover", "faces", null, "crop must be 'center', 'attention' or 'entropy'.")]
    [InlineData(800, 600, null, "attention", null, "crop is only used with fit=cover.")]
    public void TryParse_Invalid(int? width, int? height, string? fit, string? crop, bool? upscale, string expected)
    {
        Assert.False(ResizeParameters.TryParse(width, height, fit, crop, upscale, out var resize, out var error));
        Assert.Null(resize);
        Assert.Equal(expected, error);
    }
}
