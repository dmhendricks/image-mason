using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/resize?width=800&amp;height=600&amp;fit=cover: resizes to a box, optionally converting the format, and
/// optimizes the result with the same quality presets and parameters as optimize.
/// </summary>
internal static class ResizeEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        ImageMasonOptions options,
        ImageDownloader downloader,
        ImageOptimizer optimizer,
        ImageStore store,
        ILogger<ImageOptimizer> logger,
        int? width,
        int? height,
        string? fit,
        string? crop,
        string? rect,
        string? format,
        string? quality,
        double? minScore,
        string? output,
        string? outputFilename,
        string? url,
        bool? upscale,
        bool keepOriginal = false)
    {
        if (!ApiRequest.IsAuthorized(http.Request, options.ApiKey))
            return ApiRequest.Unauthorized();

        if (width is null && height is null)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Send width, height or both.");

        if (!ResizeParameters.TryParse(width, height, fit, crop, upscale, out var resize, out var resizeError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, resizeError!);

        if (!RectParameter.TryParse(rect, out var area, out var rectError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, rectError!);

        ImageFormat? target = null;
        if (format is not null)
        {
            if (!ImageFormatDetector.TryParse(format, out var parsed))
                return ApiRequest.Problem(StatusCodes.Status400BadRequest, "format must be 'jpeg', 'png', 'webp', 'avif' or 'jxl'.");
            target = parsed;
        }

        return await OptimizeEndpoint.RunAsync(http, options, downloader, optimizer, store, logger,
            target, resize, area, quality, minScore, maxWidth: null, output, outputFilename, url, preferJpeg: false, keepOriginal);
    }
}
