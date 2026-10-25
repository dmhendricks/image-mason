using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/convert?format=webp: returns the image in another format (JPEG, PNG, WebP or AVIF), optimized with
/// the same quality presets and parameters as optimize.
/// </summary>
internal static class ConvertEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        ImageMasonOptions options,
        ImageDownloader downloader,
        ImageOptimizer optimizer,
        ImageStore store,
        ILogger<ImageOptimizer> logger,
        string? format,
        string? quality,
        double? minScore,
        int? maxWidth,
        string? rect,
        string? output,
        string? outputFilename,
        string? url,
        bool keepOriginal = false)
    {
        if (!ApiRequest.IsAuthorized(http.Request, options.ApiKey))
            return ApiRequest.Unauthorized();

        if (!ImageFormatDetector.TryParse(format, out var target))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "format must be 'jpeg', 'png', 'webp' or 'avif'.");

        if (!RectParameter.TryParse(rect, out var area, out var rectError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, rectError!);

        return await OptimizeEndpoint.RunAsync(http, options, downloader, optimizer, store, logger,
            target, null, area, quality, minScore, maxWidth, output, outputFilename, url, preferJpeg: false, keepOriginal);
    }
}
