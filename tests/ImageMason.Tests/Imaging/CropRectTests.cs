using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class CropRectTests
{
    [Theory]
    [InlineData(0, 0, 640, 427, true)]
    [InlineData(80, 0, 480, 427, true)]
    [InlineData(639, 426, 1, 1, true)]
    [InlineData(600, 0, 41, 100, false)]
    [InlineData(0, 400, 100, 28, false)]
    [InlineData(int.MaxValue, 0, 1, 1, false)]
    public void FitsWithin_ChecksBothEdges(int left, int top, int width, int height, bool expected)
    {
        Assert.Equal(expected, new CropRect(left, top, width, height).FitsWithin(640, 427));
    }

    [Fact]
    public void Covers_OnlyTheWholeImage()
    {
        Assert.True(new CropRect(0, 0, 640, 427).Covers(640, 427));
        Assert.False(new CropRect(0, 0, 640, 426).Covers(640, 427));
        Assert.False(new CropRect(1, 0, 639, 427).Covers(640, 427));
    }

    [Fact]
    public void VipsArguments_AreLeftTopWidthHeight()
    {
        var rect = new CropRect(80, 5, 480, 400);

        Assert.Equal("80 5 480 400", string.Join(' ', rect.VipsArguments()));
        Assert.Equal("80,5,480,400", rect.ToString());
    }
}
