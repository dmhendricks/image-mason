using System.Diagnostics;
using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/upload: stores a JPEG, PNG, WebP, AVIF or JPEG XL unchanged and returns the same JSON as optimize?output=json.
/// The file is served at /images/{name} until IMAGE_MASON_IMAGE_TTL_MINUTES, then deleted.
/// </summary>
internal static class UploadEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        ImageMasonOptions options,
        ImageDownloader downloader,
        ImageStore store,
        ILogger<ImageStore> logger,
        string? outputFilename,
        string? url)
    {
        if (!ApiRequest.IsAuthorized(http.Request, options.ApiKey))
            return ApiRequest.Unauthorized();

        if (!ApiRequest.TryParseOutputFilename(outputFilename, out var storedName, out var nameError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, nameError!);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        byte[] input = [];
        try
        {
            input = await ApiRequest.ReadImageAsync(http.Request, downloader, url, timeout.Token);
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
        catch (BadHttpRequestException ex)
        {
            return ApiRequest.Problem(ex.StatusCode, ex.Message);
        }
        catch (ImageDownloadException ex)
        {
            logger.LogWarning("Image download failed: {Message}", ex.Message);
            return ApiRequest.Problem(ex.StatusCode, ex.Message);
        }
        catch (UnsupportedImageException ex)
        {
            return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, ex.Message);
        }
        catch (ImageTooLargeException ex)
        {
            return ApiRequest.Problem(StatusCodes.Status422UnprocessableEntity, ex.Message);
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Upload of {Bytes} bytes timed out after {Seconds} s", input.Length, options.TimeoutSeconds);
            return ApiRequest.Problem(StatusCodes.Status504GatewayTimeout, "The request timed out.");
        }
    }
}
