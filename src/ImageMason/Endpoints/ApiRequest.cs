using System.Globalization;
using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;
using Microsoft.AspNetCore.Http.Features;

namespace ImageMason.Endpoints;

/// <summary>Helpers shared by the API endpoints.</summary>
internal static class ApiRequest
{
    /// <summary>Retry-After for 503s: the job queue was full, or an image tool ran out of memory.</summary>
    public const string RetryAfterSeconds = "30";

    public static IResult Problem(int statusCode, string detail) =>
        Results.Problem(detail: detail, statusCode: statusCode);

    /// <summary>Whether <see cref="Problem(ApiServices, Exception, long, string?)"/> answers this exception; anything
    /// else is left to become a 500.</summary>
    public static bool IsExpected(Exception exception, HttpContext http) => exception switch
    {
        BadHttpRequestException or ImageDownloadException or UnsupportedImageException or CropOutsideImageException
            or ImageTooLargeException or ImageProcessingException or JobTooLargeException or ToolKilledException => true,
        // A timeout, rather than the client going away.
        OperationCanceledException => !http.RequestAborted.IsCancellationRequested,
        _ => false,
    };

    /// <summary>The Problem Details for an exception the API expects (see <see cref="IsExpected"/>), shared by every
    /// endpoint that reads an image.</summary>
    /// <param name="inputBytes">The image's size for the log, or 0 before it was read.</param>
    /// <param name="job">"Request" or "Upload", to log a timeout as "Request of N bytes timed out"; null logs none.</param>
    public static IResult Problem(ApiServices api, Exception exception, long inputBytes, string? job)
    {
        var logger = api.Logger;
        switch (exception)
        {
            case BadHttpRequestException ex:
                return Problem(ex.StatusCode, ex.Message);
            case ImageDownloadException ex:
                logger.LogWarning("Image download failed: {Message}", ex.Message);
                return Problem(ex.StatusCode, ex.Message);
            case UnsupportedImageException:
                return Problem(StatusCodes.Status415UnsupportedMediaType, exception.Message);
            case CropOutsideImageException:
                return Problem(StatusCodes.Status400BadRequest, exception.Message);
            case ImageTooLargeException or ImageProcessingException:
                return Problem(StatusCodes.Status422UnprocessableEntity, exception.Message);
            case JobTooLargeException ex:
                logger.LogWarning("Rejected a job needing {Needed} with {Budget} for jobs", ByteSizes.ToGigabytes(ex.NeededBytes), ByteSizes.ToGigabytes(ex.BudgetBytes));
                return Problem(StatusCodes.Status422UnprocessableEntity, ex.Message);
            case ToolKilledException ex:
                // Never fall back to a weaker result: the caller asked for this preset and should know it failed.
                logger.LogError("{Tool} was killed while optimizing {Bytes} bytes; the container is out of memory. Raise its memory limit or lower {Setting}",
                    ex.Tool, inputBytes, ImageMasonOptions.Prefix + "MEMORY_LIMIT");
                api.Http.Response.Headers.RetryAfter = RetryAfterSeconds;
                return Problem(StatusCodes.Status503ServiceUnavailable,
                    "The server ran out of memory while optimizing this image. Retry later, or send a smaller image or maxWidth.");
            case OperationCanceledException:
                if (job is not null)
                    logger.LogWarning("{Job} of {Bytes} bytes timed out after {Seconds} s", job, inputBytes, api.Options.TimeoutSeconds);
                return Problem(StatusCodes.Status504GatewayTimeout, "The request timed out.");
            default:
                throw new ArgumentException("Not an exception the API expects.", nameof(exception), exception);
        }
    }

    /// <summary>"format must be 'png', 'webp', 'jpeg', 'jxl' or 'avif'.", from the supported formats.</summary>
    private static readonly string FormatError = "format must be "
        + string.Join(", ", ImageFormatDetector.Supported.SkipLast(1).Select(format => $"'{format.Name()}'"))
        + $" or '{ImageFormatDetector.Supported[^1].Name()}'.";

    /// <summary>
    /// Parses the format parameter, or returns why it can't be used. Null keeps the input's format, unless
    /// <paramref name="required"/>.
    /// </summary>
    public static bool TryParseFormat(string? value, bool required, out ImageFormat? format, out string? error)
    {
        format = null;
        error = null;
        if (value is null && !required)
            return true;

        if (ImageFormatDetector.TryParse(value, out var parsed))
            format = parsed;
        else
            error = FormatError;

        return error is null;
    }

    public static string Origin(HttpRequest request, string? publicBaseUrl) =>
        !string.IsNullOrEmpty(publicBaseUrl) ? publicBaseUrl : $"{request.Scheme}://{request.Host.Value}";

    /// <summary>Trims the requested stored name, or returns why it can't be used. Empty means "generate one".</summary>
    public static bool TryParseOutputFilename(string? value, out string? name, out string? error)
    {
        name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        error = name is null || ImageStore.IsStoredName(name)
            ? null
            : "outputFilename must be letters, digits, '_' or '-', then .jpg, .png, .webp, .avif or .jxl.";
        return error is null;
    }

    /// <summary>
    /// The image is the request body, the file at <paramref name="url"/>, or the stored image named
    /// <paramref name="source"/> (a name from upload, so a caller can run several jobs on one image without sending
    /// it each time); only one of them. Pass a null <paramref name="store"/> where source isn't accepted.
    /// </summary>
    /// <exception cref="ImageDownloadException">None or more than one was sent, the download failed, or no stored image
    /// has that name (404).</exception>
    public static async Task<byte[]> ReadImageAsync(HttpRequest request, ImageDownloader downloader, ImageStore? store, string? url,
        string? source, CancellationToken cancellationToken)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(url);
        var hasSource = store is not null && !string.IsNullOrWhiteSpace(source);
        var ways = store is null ? "the body or url" : "the body, url or source";
        if (hasUrl || hasSource)
        {
            var hasBody = request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                && request.ContentLength is not 0;
            if (hasBody || (hasUrl && hasSource))
                throw new ImageDownloadException($"Send the image one way: as {ways}.", StatusCodes.Status400BadRequest);
        }

        if (hasSource)
            return await ReadStoredAsync(store!, source!.Trim(), cancellationToken);

        if (hasUrl)
            return await downloader.DownloadAsync(url!.Trim(), cancellationToken);

        // A declared length within Kestrel's body limit sizes the buffer exactly: one allocation, no copy. A longer one
        // is left to the copy below, where Kestrel rejects it with 413 before reading much.
        var maxBytes = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
        if (request.ContentLength is long length and > 0 && length <= maxBytes)
        {
            var data = new byte[length];
            await request.Body.ReadExactlyAsync(data, cancellationToken);
            return data;
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
            throw new ImageDownloadException($"The request body is empty; send the image as {ways}.", StatusCodes.Status400BadRequest);

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadStoredAsync(ImageStore store, string name, CancellationToken cancellationToken)
    {
        const string missing = "No stored image has that name; it may have expired. Upload it again, or send it as the body.";
        if (!store.TryGet(name, out var file))
            throw new ImageDownloadException(missing, StatusCodes.Status404NotFound);

        try
        {
            return await File.ReadAllBytesAsync(file.Path, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Expired and deleted since TryGet.
            throw new ImageDownloadException(missing, StatusCodes.Status404NotFound);
        }
    }

    /// <summary>A UTC instant in the container timezone, as yyyy-MM-ddTHH:mm:ss±hh:mm.</summary>
    public static string LocalTimestamp(DateTime utc) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
