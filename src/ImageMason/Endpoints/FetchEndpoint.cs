using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/fetch: downloads the image at url and returns it unchanged. For clients that can't read another
/// site's images themselves, like the web interface (browsers keep pages from reading most cross-origin images).
/// The same limits apply as for url on the other endpoints (IMAGE_MASON_URL_*).
/// </summary>
internal static class FetchEndpoint
{
    public static async Task<IResult> HandleAsync([AsParameters] ApiServices api, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Pass url, the address of the image to download.");

        using var timeout = api.StartTimeout();

        try
        {
            var input = await api.Downloader.DownloadAsync(url.Trim(), timeout.Token);
            var format = ImageFormatDetector.Detect(input);
            if (format == ImageFormat.Unknown)
                return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, $"That address isn't a {ImageFormatDetector.SupportedNames} image.");

            api.Http.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(input, format.ContentType());
        }
        catch (Exception ex) when (ApiRequest.IsExpected(ex, api.Http))
        {
            return ApiRequest.Problem(api, ex, inputBytes: 0, job: null);
        }
    }
}
