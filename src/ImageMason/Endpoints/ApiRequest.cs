using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageMason.Http;
using ImageMason.Storage;
using Microsoft.AspNetCore.Http.Features;

namespace ImageMason.Endpoints;

/// <summary>Helpers shared by the API endpoints.</summary>
internal static class ApiRequest
{
    public static IResult Problem(int statusCode, string detail) =>
        Results.Problem(detail: detail, statusCode: statusCode);

    public static IResult Unauthorized() =>
        Problem(StatusCodes.Status401Unauthorized, "Send a valid API key in the X-Api-Key header.");

    // Hashing first makes the comparison constant-time regardless of key length.
    public static bool IsAuthorized(HttpRequest request, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return true;

        var provided = SHA256.HashData(Encoding.UTF8.GetBytes(request.Headers["X-Api-Key"].ToString()));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    public static string Origin(HttpRequest request, string? publicBaseUrl) =>
        !string.IsNullOrEmpty(publicBaseUrl) ? publicBaseUrl : $"{request.Scheme}://{request.Host.Value}";

    /// <summary>Trims the requested stored name, or returns why it can't be used. Empty means "generate one".</summary>
    public static bool TryParseOutputFilename(string? value, out string? name, out string? error)
    {
        name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        error = name is null || ImageStore.IsStoredName(name)
            ? null
            : "outputFilename must be letters, digits, '_' or '-', then .jpg, .png, .webp or .avif.";
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
