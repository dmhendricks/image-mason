using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/crop?rect=80,0,480,480: keeps the given area, optionally resizing or converting it, and optimizes the
/// result. The same parameters as optimize, with rect required.
/// </summary>
internal static class CropEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        ImageMasonOptions options,
        ImageDownloader downloader,
        ImageOptimizer optimizer,
        ImageStore store,
        ILogger<ImageOptimizer> logger,
        string? rect,
        string? format,
        string? quality,
        double? minScore,
        int? maxWidth,
        int? width,
        int? height,
        string? fit,
        string? crop,
        string? output,
        string? outputFilename,
        string? url,
        bool? upscale,
        bool preferJpeg = false,
        bool keepOriginal = false)
    {
        if (string.IsNullOrWhiteSpace(rect))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Send rect as left,top,width,height, such as 80,0,480,480.");

        return await OptimizeEndpoint.HandleAsync(http, options, downloader, optimizer, store, logger, format, quality,
            minScore, maxWidth, width, height, fit, crop, rect, output, outputFilename, url, upscale, preferJpeg, keepOriginal);
    }
}
