using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/fetch: downloads the image at url and returns it unchanged. For clients that can't read another
/// site's images themselves, like the web interface (browsers keep pages from reading most cross-origin images).
/// The same limits apply as for url on the other endpoints (IMAGE_MASON_URL_*).
/// </summary>
internal static class FetchEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        ImageMasonOptions options,
        ImageDownloader downloader,
        ILogger<ImageDownloader> logger,
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Pass url, the address of the image to download.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            var input = await downloader.DownloadAsync(url.Trim(), timeout.Token);
            var format = ImageFormatDetector.Detect(input);
            if (format == ImageFormat.Unknown)
                return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, $"That address isn't a {ImageFormatDetector.SupportedNames} image.");

            http.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(input, format.ContentType());
        }
        catch (ImageDownloadException ex)
        {
            logger.LogWarning("Image download failed: {Message}", ex.Message);
            return ApiRequest.Problem(ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            return ApiRequest.Problem(StatusCodes.Status504GatewayTimeout, "The request timed out.");
        }
    }
}
