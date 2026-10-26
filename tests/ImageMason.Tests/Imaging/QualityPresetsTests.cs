using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class QualityPresetsTests
{
    [Theory]
    [InlineData("lossless", QualityPreset.Lossless)]
    [InlineData("High", QualityPreset.High)]
    [InlineData("medium", QualityPreset.Medium)]
    [InlineData("LOW", QualityPreset.Low)]
    public void TryParse_AcceptsPresetNames(string value, QualityPreset expected)
    {
        Assert.True(QualityPresets.TryParse(value, out var preset));
        Assert.Equal(expected, preset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" high")]
    [InlineData("best")]
    [InlineData("1")]
    public void TryParse_RejectsOtherValues(string? value)
    {
        Assert.False(QualityPresets.TryParse(value, out _));
    }
}
