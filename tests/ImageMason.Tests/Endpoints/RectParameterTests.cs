using ImageMason.Endpoints;
using ImageMason.Imaging;

namespace ImageMason.Tests.Endpoints;

public sealed class RectParameterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryParse_NothingSent_HasNoRect(string? value)
    {
        Assert.True(RectParameter.TryParse(value, out var rect, out var error));
        Assert.Null(rect);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("80,0,480,480", 80, 0, 480, 480)]
    [InlineData(" 1, 2 ,3 , 4 ", 1, 2, 3, 4)]
    [InlineData("0,0,65535,1", 0, 0, 65535, 1)]
    public void TryParse_Valid(string value, int left, int top, int width, int height)
    {
        Assert.True(RectParameter.TryParse(value, out var rect, out _));
        Assert.Equal(new CropRect(left, top, width, height), rect);
    }

    [Theory]
    [InlineData("80,0,480", "rect must be left,top,width,height in whole pixels, such as 80,0,480,480.")]
    [InlineData("80,0,480,480,1", "rect must be left,top,width,height in whole pixels, such as 80,0,480,480.")]
    [InlineData("-1,0,480,480", "rect must be left,top,width,height in whole pixels, such as 80,0,480,480.")]
    [InlineData("80,0,480.5,480", "rect must be left,top,width,height in whole pixels, such as 80,0,480,480.")]
    [InlineData("80,,480,480", "rect must be left,top,width,height in whole pixels, such as 80,0,480,480.")]
    [InlineData("a,b,c,d", "rect must be left,top,width,height in whole pixels, such as 80,0,480,480.")]
    [InlineData("80,0,0,480", "rect's width and height must be between 1 and 65535.")]
    [InlineData("80,0,480,65536", "rect's width and height must be between 1 and 65535.")]
    public void TryParse_Invalid(string value, string expected)
    {
        Assert.False(RectParameter.TryParse(value, out var rect, out var error));
        Assert.Null(rect);
        Assert.Equal(expected, error);
    }
}
