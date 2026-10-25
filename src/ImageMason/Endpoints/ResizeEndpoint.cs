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
    private const int MaxSide = 65535;

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
        string? format,
        string? quality,
        double? minScore,
        string? output,
        string? outputFilename,
        string? url,
        bool upscale = false,
        bool keepOriginal = false)
    {
        if (!ApiRequest.IsAuthorized(http.Request, options.ApiKey))
            return ApiRequest.Unauthorized();

        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, $"width and height must be between 1 and {MaxSide}.");

        if (width is null && height is null)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Send width, height or both.");

        if (!TryParseFit(fit, out var resizeFit))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "fit must be 'contain', 'cover' or 'fill'.");

        if (resizeFit != ResizeFit.Contain && (width is null || height is null))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, $"fit={resizeFit.ToString().ToLowerInvariant()} needs both width and height.");

        if (!TryParseCrop(crop, out var position))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "crop must be 'center', 'attention' or 'entropy'.");

        if (crop is not null && resizeFit != ResizeFit.Cover)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "crop is only used with fit=cover.");

        ImageFormat? target = null;
        if (format is not null)
        {
            if (!ImageFormatDetector.TryParse(format, out var parsed))
                return ApiRequest.Problem(StatusCodes.Status400BadRequest, "format must be 'jpeg', 'png', 'webp' or 'avif'.");
            target = parsed;
        }

        var resize = new ResizeRequest(width, height, resizeFit, upscale, position);
        return await OptimizeEndpoint.RunAsync(http, options, downloader, optimizer, store, logger,
            target, resize, quality, minScore, maxWidth: null, output, outputFilename, url, preferJpeg: false, keepOriginal);
    }

    private static bool TryParseFit(string? value, out ResizeFit fit)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "contain":
                fit = ResizeFit.Contain;
                return true;
            case "cover":
                fit = ResizeFit.Cover;
                return true;
            case "fill":
                fit = ResizeFit.Fill;
                return true;
            default:
                fit = default;
                return false;
        }
    }

    private static bool TryParseCrop(string? value, out CropPosition crop)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "center" or "centre":
                crop = CropPosition.Center;
                return true;
            case "attention":
                crop = CropPosition.Attention;
                return true;
            case "entropy":
                crop = CropPosition.Entropy;
                return true;
            default:
                crop = default;
                return false;
        }
    }
}
