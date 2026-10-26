using System.Net;
using ImageMason.Configuration;

namespace ImageMason.Http;

/// <summary>
/// Downloads an image for the url parameter. Redirects are followed here rather than by HttpClient, so that every
/// hop is checked against <see cref="UrlFetchPolicy"/>. The caller still checks that the bytes are a supported image.
/// </summary>
public sealed class ImageDownloader : IDisposable
{
    private const int MaxRedirects = 5;

    private readonly HttpClient _http;
    private readonly UrlFetchPolicy _policy;
    private readonly long _maxBytes;

    public ImageDownloader(UrlFetchPolicy policy, ImageMasonOptions options)
    {
        _policy = policy;
        _maxBytes = options.MaxInputBytes;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
    }

    /// <exception cref="ImageDownloadException">The URL is not allowed, or the download failed or was too large.</exception>
    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!_policy.Enabled)
            throw new ImageDownloadException("Fetching images by URL is turned off on this server; send the image as the request body.", StatusCodes.Status400BadRequest);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ImageDownloadException("url must be an absolute http or https URL.", StatusCodes.Status400BadRequest);

        for (var redirects = 0; ; redirects++)
        {
            if (_policy.Check(uri) is { } reason)
                throw new ImageDownloadException(redirects == 0 ? reason : $"url redirected to {uri}. {reason}", StatusCodes.Status400BadRequest);

            using var response = await SendAsync(uri, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (redirects == MaxRedirects)
                    throw new ImageDownloadException($"url redirected more than {MaxRedirects} times.", StatusCodes.Status502BadGateway);

                var location = response.Headers.Location
                    ?? throw new ImageDownloadException("url redirected without a Location header.", StatusCodes.Status502BadGateway);
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new ImageDownloadException($"Downloading the image returned {(int)response.StatusCode}.", StatusCodes.Status502BadGateway);

            return await ReadBodyAsync(response, cancellationToken);
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ImageDownloadException("Could not download the image.", StatusCodes.Status502BadGateway);
        }
    }

    private async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var length = response.Content.Headers.ContentLength;
        if (length > _maxBytes)
            throw TooLarge();

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // A declared length sizes the buffer exactly: one allocation, no copy.
            if (length > 0)
            {
                var data = new byte[length.Value];
                await stream.ReadExactlyAsync(data, cancellationToken);
                return data;
            }

            return await ReadUnsizedAsync(stream, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            // The connection dropped, or the body ended before its declared length.
            throw new ImageDownloadException("Could not download the image.", StatusCodes.Status502BadGateway);
        }
    }

    private async Task<byte[]> ReadUnsizedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;
            if (buffer.Length + read > _maxBytes)
                throw TooLarge();
            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
            throw new ImageDownloadException("The URL returned an empty body.", StatusCodes.Status400BadRequest);

        return buffer.ToArray();
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static ImageDownloadException TooLarge() =>
        new("The image at url is larger than the upload limit.", StatusCodes.Status413PayloadTooLarge);
}

public sealed class ImageDownloadException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
