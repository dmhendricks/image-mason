using System.Diagnostics;
using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/upload: stores a JPEG, PNG, WebP, AVIF or JPEG XL unchanged and returns the same JSON as optimize?output=json.
/// The file is served at /images/{name} until IMAGE_MASON_IMAGE_TTL_MINUTES, then deleted.
/// </summary>
internal static class UploadEndpoint
{
    public static async Task<IResult> HandleAsync([AsParameters] ApiServices api, string? outputFilename, string? url)
    {
        var (http, options, downloader, _, store, logger) = api;

        if (!ApiRequest.TryParseOutputFilename(outputFilename, out var storedName, out var nameError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, nameError!);

        using var timeout = api.StartTimeout();

        var stopwatch = Stopwatch.StartNew();
        byte[] input = [];
        try
        {
            input = await ApiRequest.ReadImageAsync(http.Request, downloader, store: null, url, source: null, timeout.Token);
            var format = ImageFormatDetector.Detect(input);
            if (format == ImageFormat.Unknown)
                return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, $"Unsupported image format; expected {ImageFormatDetector.SupportedNames}.");

            if (storedName is not null && !storedName.EndsWith(format.Extension(), StringComparison.Ordinal))
                return ApiRequest.Problem(StatusCodes.Status400BadRequest, $"outputFilename must end in {format.Extension()}.");

            var info = ImageOptimizer.ReadInfo(input, format, options.MaxPixels);
            var saved = await store.SaveAsync(input, format, storedName, timeout.Token);
            var elapsedMs = (long)stopwatch.Elapsed.TotalMilliseconds;
            var outputUrl = $"{ApiRequest.Origin(http.Request, options.PublicBaseUrl)}/images/{saved.Name}";
            logger.LogInformation("Uploaded {Name} ({Bytes} bytes) at {Url}", saved.Name, input.Length, outputUrl);

            return Results.Ok(new HostedImageResponse
            {
                Output = outputUrl,
                MimeType = format.ContentType(),
                OriginalSize = input.LongLength,
                CompressedSize = input.LongLength,
                SizeChangePercent = 0,
                OriginalWidth = info.Width,
                OriginalHeight = info.Height,
                Width = info.Width,
                Height = info.Height,
                Quality = "original",
                Method = "upload",
                ExpiresAt = ApiRequest.LocalTimestamp(saved.ExpiresAtUtc),
                DurationMs = elapsedMs,
            });
        }
        catch (Exception ex) when (ApiRequest.IsExpected(ex, http))
        {
            return ApiRequest.Problem(api, ex, input.Length, "Upload");
        }
    }
}
