using ImageMason.Configuration;

namespace ImageMason.Tests.Configuration;

public sealed class ByteSizesTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1048576", 1_048_576L)]
    [InlineData("512k", 524_288L)]
    [InlineData("25m", 26_214_400L)]
    [InlineData("25MB", 26_214_400L)]
    [InlineData("1.5g", 1_610_612_736L)]
    [InlineData("10 GiB", 10_737_418_240L)]
    [InlineData("1t", 1_099_511_627_776L)]
    public void TryParse_AcceptsByteCountsAndSuffixes(string text, long expected)
    {
        Assert.True(ByteSizes.TryParse(text, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("m")]
    [InlineData("-5m")]
    [InlineData("10x")]
    [InlineData("ten")]
    [InlineData("99999999999t")]
    public void TryParse_RejectsInvalidSizes(string? text)
    {
        Assert.False(ByteSizes.TryParse(text, out _));
    }
}
