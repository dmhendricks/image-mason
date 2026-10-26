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

    /// <summary>The image is the request body, or the file at <paramref name="url"/>; not both.</summary>
    /// <exception cref="ImageDownloadException">Both or neither were sent, or the download failed.</exception>
    public static async Task<byte[]> ReadImageAsync(HttpRequest request, ImageDownloader downloader, string? url, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            var hasBody = request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                && request.ContentLength is not 0;
            if (hasBody)
                throw new ImageDownloadException("Send the image as the body or as url, not both.", StatusCodes.Status400BadRequest);

            return await downloader.DownloadAsync(url.Trim(), cancellationToken);
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
            throw new ImageDownloadException("The request body is empty; send the image bytes as the body, or pass url.", StatusCodes.Status400BadRequest);

        return buffer.ToArray();
    }

    /// <summary>A UTC instant in the container timezone, as yyyy-MM-ddTHH:mm:ss±hh:mm.</summary>
    public static string LocalTimestamp(DateTime utc) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
