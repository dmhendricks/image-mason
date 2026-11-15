using System.Globalization;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/preview: a copy of an image to display, for a client that can't show its format, such as a browser and
/// a HEIC photo. Upright, fitted within size pixels (never enlarged), as WebP. X-Image-Mason-Display-Dimensions gives
/// the original's size as displayed, which the copy is usually smaller than.
/// It exists for the web interface, so it's documented in the README for other web interfaces, marked as one that may
/// change with it, and left out of /docs.
/// </summary>
internal static class PreviewEndpoint
{
    /// <summary>Without size: about a screen's width.</summary>
    private const int DefaultSize = 2048;

    /// <summary>Larger is refused, since a preview is for looking at, not for keeping.</summary>
    private const int MaxSize = 4096;

    public static async Task<IResult> HandleAsync([AsParameters] ApiServices api, string? url, string? source, int? size)
    {
        if (size is < 1 or > MaxSize)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, $"size must be between 1 and {MaxSize}.");

        using var timeout = api.StartTimeout();

        byte[] input = [];
        try
        {
            input = await ApiRequest.ReadImageAsync(api.Http.Request, api.Downloader, api.Store, url, source, timeout.Token);
            var preview = await api.Optimizer.PreviewAsync(input, size ?? DefaultSize, timeout.Token);

            var headers = api.Http.Response.Headers;
            headers.CacheControl = "no-store";
            headers["X-Image-Mason-Display-Dimensions"] = string.Create(CultureInfo.InvariantCulture,
                $"{preview.Info.DisplayWidth}x{preview.Info.DisplayHeight}");
            return Results.Bytes(preview.Data, "image/webp");
        }
        catch (Exception ex) when (ApiRequest.IsExpected(ex, api.Http))
        {
            return ApiRequest.Problem(api, ex, input.Length, "Preview");
        }
    }
}
