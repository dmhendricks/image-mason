using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class MemoryEstimateTests
{
    private const long MiB = 1024 * 1024;

    // Peak container memory measured for one job at quality=high, and the job's shape.
    [Theory]
    [InlineData("12 MP JPEG, high", ImageFormat.Jpeg, ImageFormat.Jpeg, 12_000_000, 12_000_000, true, 1760)]
    [InlineData("48 MP JPEG, high", ImageFormat.Jpeg, ImageFormat.Jpeg, 48_000_000, 48_000_000, true, 6928)]
    [InlineData("24 MP PNG photo, high", ImageFormat.Png, ImageFormat.Png, 24_000_000, 24_000_000, true, 4099)]
    [InlineData("48 MP PNG graphic, high", ImageFormat.Png, ImageFormat.Png, 48_000_000, 48_000_000, true, 7020)]
    [InlineData("48 MP JPEG, lossless", ImageFormat.Jpeg, ImageFormat.Jpeg, 48_000_000, 48_000_000, false, 508)]
    [InlineData("48 MP PNG graphic, lossless", ImageFormat.Png, ImageFormat.Png, 48_000_000, 48_000_000, false, 1789)]
    [InlineData("48 MP JPEG resized to 12 MP, high", ImageFormat.Jpeg, ImageFormat.Jpeg, 48_000_000, 12_000_000, true, 1824)]
    [InlineData("15.6 MP JPEG to WebP, high", ImageFormat.Jpeg, ImageFormat.WebP, 15_564_300, 15_564_300, true, 2496)]
    [InlineData("15.6 MP PNG to WebP, high", ImageFormat.Png, ImageFormat.WebP, 15_564_300, 15_564_300, true, 2633)]
    [InlineData("15.6 MP JPEG to AVIF, high", ImageFormat.Jpeg, ImageFormat.Avif, 15_564_300, 15_564_300, true, 2435)]
    [InlineData("15.6 MP AVIF to JPEG, high", ImageFormat.Avif, ImageFormat.Jpeg, 15_564_300, 15_564_300, true, 2335)]
    [InlineData("15.6 MP JPEG to WebP, lossless", ImageFormat.Jpeg, ImageFormat.WebP, 15_564_300, 15_564_300, false, 415)]
    [InlineData("15.6 MP JPEG to AVIF, lossless", ImageFormat.Jpeg, ImageFormat.Avif, 15_564_300, 15_564_300, false, 530)]
    public void Estimate_IsAtLeastTheMeasuredPeak_ButNotFarAbove(string job, ImageFormat format, ImageFormat target,
        long inputPixels, long outputPixels, bool scored, long measuredMiB)
    {
        var estimate = ImageOptimizer.EstimateMemory(format, target, inputPixels, outputPixels, scored);
        var measured = measuredMiB * MiB;

        Assert.True(estimate >= measured, $"{job}: estimate {estimate / MiB} MiB is below the measured {measuredMiB} MiB");
        Assert.True(estimate <= measured * 1.6, $"{job}: estimate {estimate / MiB} MiB is far above the measured {measuredMiB} MiB");
    }
}
