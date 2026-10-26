using ImageMason.Configuration;
using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>
/// GET /api/v1/limits: the formats, sizes, presets and timeouts this server is configured with. Needs no API key,
/// so clients (such as the web interface) can learn whether they need one before asking for it.
/// </summary>
internal static class LimitsEndpoint
{
    public static IResult Handle(ImageMasonOptions options)
    {
        return Results.Ok(new LimitsResponse(
            Formats: [.. ImageFormatDetector.Supported.Select(f => new FormatLimits(f.Name(), f.ContentType(), f.Extension(), ImageOptimizer.MaxSide(f)))],
            MaxInputSize: options.MaxInputBytes,
            MaxPixels: options.MaxPixels,
            Presets: new Dictionary<string, double?>
            {
                ["lossless"] = null,
                ["high"] = options.HighMinScore,
                ["medium"] = options.MediumMinScore,
                ["low"] = options.LowMinScore,
            },
            DefaultQuality: options.DefaultQuality.ToString().ToLowerInvariant(),
            DefaultMaxWidth: options.DefaultMaxWidth,
            ImageTtlMinutes: options.ImageTtlMinutes,
            TimeoutSeconds: options.TimeoutSeconds,
            UrlFetch: options.UrlFetchEnabled,
            ApiKeyRequired: !string.IsNullOrEmpty(options.ApiKey)));
    }
}
