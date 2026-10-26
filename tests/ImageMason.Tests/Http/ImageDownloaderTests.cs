using System.Net;
using System.Net.Sockets;
using ImageMason.Configuration;
using ImageMason.Http;
using Microsoft.AspNetCore.Http;

namespace ImageMason.Tests.Http;

public sealed class ImageDownloaderTests
{
    [Fact]
    public async Task PrivateAddressesOff_RefusesLoopback()
    {
        await using var server = await ImageServer.StartAsync(HttpStatusCode.OK);
        using var downloader = Downloader(allowPrivate: false);

        var error = await Assert.ThrowsAsync<ImageDownloadException>(
            () => downloader.DownloadAsync(server.Url, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal(0, server.Requests);
    }

    [Theory]
    [InlineData("http://[::1]:9/photo.jpg")]
    [InlineData("http://localhost:9/photo.jpg")]
    [InlineData("http://[::ffff:169.254.169.254]/latest/meta-data")]
    public async Task PrivateAddressesOff_RefusesBeforeConnecting(string url)
    {
        using var downloader = Downloader(allowPrivate: false);

        var error = await Assert.ThrowsAsync<ImageDownloadException>(
            () => downloader.DownloadAsync(url, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
    }

    [Fact]
    public async Task PrivateAddressesOn_Downloads()
    {
        await using var server = await ImageServer.StartAsync(HttpStatusCode.OK);
        using var downloader = Downloader(allowPrivate: true);

        Assert.Equal(ImageServer.Body, await downloader.DownloadAsync(server.Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ErrorStatus_IsNotEchoed()
    {
        await using var server = await ImageServer.StartAsync(HttpStatusCode.NotFound);
        using var downloader = Downloader(allowPrivate: true);

        var error = await Assert.ThrowsAsync<ImageDownloadException>(
            () => downloader.DownloadAsync(server.Url, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status502BadGateway, error.StatusCode);
        Assert.DoesNotContain("404", error.Message, StringComparison.Ordinal);
    }

    private static ImageDownloader Downloader(bool allowPrivate)
    {
        var options = new ImageMasonOptions { UrlAllowPrivate = allowPrivate };
        return new ImageDownloader(new UrlFetchPolicy(options), options);
    }

    /// <summary>A one-route HTTP server on 127.0.0.1 that answers every request with <paramref name="status"/>.</summary>
    private sealed class ImageServer : IAsyncDisposable
    {
        public static readonly byte[] Body = [1, 2, 3, 4];

        private readonly TcpListener _listener;
        private readonly Task _loop;
        private readonly CancellationTokenSource _stop = new();
        private int _requests;

        private ImageServer(TcpListener listener, HttpStatusCode status)
        {
            _listener = listener;
            _loop = ServeAsync(status);
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/photo.jpg";

        public int Requests => _requests;

        public static Task<ImageServer> StartAsync(HttpStatusCode status)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new ImageServer(listener, status));
        }

        private async Task ServeAsync(HttpStatusCode status)
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _requests);
                    await using var stream = client.GetStream();
                    var buffer = new byte[4096];
                    _ = await stream.ReadAsync(buffer, _stop.Token);

                    var body = status == HttpStatusCode.OK ? Body : [];
                    var head = $"HTTP/1.1 {(int)status} {status}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head), _stop.Token);
                    await stream.WriteAsync(body, _stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
            _stop.Dispose();
        }
    }
}
