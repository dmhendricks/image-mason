using ImageMason.Configuration;
using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>GET /api/v1/limits: the formats, sizes, presets and timeouts this server is configured with.</summary>
internal static class LimitsEndpoint
{
    public static IResult Handle(HttpContext http, ImageMasonOptions options)
    {
        if (!ApiRequest.IsAuthorized(http.Request, options.ApiKey))
            return ApiRequest.Unauthorized();

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
            DefaultMaxWidth: options.DefaultMaxWidth,
            ImageTtlMinutes: options.ImageTtlMinutes,
            TimeoutSeconds: options.TimeoutSeconds,
            UrlFetch: options.UrlFetchEnabled,
            ApiKeyRequired: !string.IsNullOrEmpty(options.ApiKey)));
    }
}
