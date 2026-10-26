namespace ImageMason.Imaging;

public static class QualityPresets
{
    /// <summary>Parses a preset name from a request or setting: lossless, high, medium or low. Case-insensitive.</summary>
    public static bool TryParse(string? value, out QualityPreset preset)
    {
        switch (value?.ToLowerInvariant())
        {
            case "lossless":
                preset = QualityPreset.Lossless;
                return true;
            case "high":
                preset = QualityPreset.High;
                return true;
            case "medium":
                preset = QualityPreset.Medium;
                return true;
            case "low":
                preset = QualityPreset.Low;
                return true;
            default:
                preset = default;
                return false;
        }
    }
}
