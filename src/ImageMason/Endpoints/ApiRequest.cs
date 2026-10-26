using System.Globalization;
using ImageMason.Http;
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
