using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>POST /api/v1/info: format, dimensions and other header details of an image, without decoding it.</summary>
internal static class InfoEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        ImageMasonOptions options,
        ImageDownloader downloader,
        ILogger<ImageDownloader> logger,
        string? url)
    {
        if (!ApiRequest.IsAuthorized(http.Request, options.ApiKey))
            return ApiRequest.Unauthorized();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            var input = await ApiRequest.ReadImageAsync(http.Request, downloader, url, timeout.Token);
            var format = ImageFormatDetector.Detect(input);
            if (format == ImageFormat.Unknown)
                return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, $"Unsupported image format; expected {ImageFormatDetector.SupportedNames}.");

            var info = ImageInfo.Read(input, format);
            if (info.Width <= 0 || info.Height <= 0)
                return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, "Could not read the image dimensions.");

            var details = ImageHeader.ReadDetails(input, format);
            var withinLimit = info.PixelCount <= options.MaxPixels;
            return Results.Ok(new ImageInfoResponse
            {
                Format = format.Name(),
                MimeType = format.ContentType(),
                Size = input.LongLength,
                Width = info.Width,
                Height = info.Height,
                Orientation = info.Orientation,
                DisplayWidth = info.DisplayWidth,
                DisplayHeight = info.DisplayHeight,
                Megapixels = Math.Round(info.PixelCount / 1e6, 2),
                BitDepth = details.BitDepth,
                HasAlpha = details.HasAlpha,
                HasIccProfile = details.HasIccProfile,
                Animated = info.Animated,
                IsPhoto = format == ImageFormat.Png && withinLimit ? PngPhoto.IsPhoto(input, options.MaxPixels, timeout.Token) : null,
                WithinPixelLimit = withinLimit,
                Exif = ExifReader.Read(input, format),
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
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            return ApiRequest.Problem(StatusCodes.Status504GatewayTimeout, "The request timed out.");
        }
    }
}
