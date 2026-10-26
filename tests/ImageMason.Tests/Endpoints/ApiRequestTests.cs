using ImageMason.Configuration;
using ImageMason.Endpoints;
using ImageMason.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace ImageMason.Tests.Endpoints;

public sealed class ApiRequestTests
{
    private static readonly ImageMasonOptions Options = new();
    private static readonly ImageDownloader Downloader = new(new UrlFetchPolicy(Options), Options);

    /// <summary>A request with this body, as Kestrel presents it: with its body size limit set.</summary>
    private static HttpRequest Request(byte[] body, long? contentLength, long? maxBodySize = 25L * 1024 * 1024)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(new MaxBodySize { MaxRequestBodySize = maxBodySize });
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = contentLength;
        return context.Request;
    }

    [Fact]
    public async Task ReadImageAsync_DeclaredLength_ReadsTheBody()
    {
        byte[] body = [1, 2, 3, 4, 5];

        var read = await ApiRequest.ReadImageAsync(Request(body, body.Length), Downloader, null, null, null, CancellationToken.None);

        Assert.Equal(body, read);
    }

    [Fact]
    public async Task ReadImageAsync_BodyShorterThanDeclared_Throws()
    {
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            ApiRequest.ReadImageAsync(Request([1, 2, 3], 10), Downloader, null, null, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5L)]
    public async Task ReadImageAsync_UnknownOrOverLimitLength_CopiesTheBody(long? maxBodySize)
    {
        byte[] body = [9, 8, 7, 6, 5, 4];

        // No Content-Length (chunked), or one over the limit, which Kestrel rejects while the body is read.
        Assert.Equal(body, await ApiRequest.ReadImageAsync(Request(body, null, maxBodySize), Downloader, null, null, null, CancellationToken.None));
        Assert.Equal(body, await ApiRequest.ReadImageAsync(Request(body, body.Length, maxBodySize), Downloader, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ReadImageAsync_EmptyBody_Throws400()
    {
        var ex = await Assert.ThrowsAsync<ImageDownloadException>(() =>
            ApiRequest.ReadImageAsync(Request([], 0), Downloader, null, null, null, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    private sealed class MaxBodySize : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}
