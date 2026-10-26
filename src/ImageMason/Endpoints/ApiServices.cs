using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;
using Microsoft.AspNetCore.Mvc;

namespace ImageMason.Endpoints;

/// <summary>
/// The request and services every API endpoint uses, bound with [AsParameters]. The services are marked
/// [FromServices]: inferred ones make the Request Delegate Generator declare a JSON body, and routing then answers
/// any other Content-Type with 415.
/// </summary>
/// <param name="Logger">The API's logger, registered in Program.cs under <see cref="LogCategory"/>.</param>
internal sealed record ApiServices(
    HttpContext Http,
    [FromServices] ImageMasonOptions Options,
    [FromServices] ImageDownloader Downloader,
    [FromServices] ImageOptimizer Optimizer,
    [FromServices] ImageStore Store,
    [FromKeyedServices(ApiServices.LogCategory)] ILogger Logger)
{
    /// <summary>The log category for every API endpoint, and the key its logger is registered under.</summary>
    public const string LogCategory = "ImageMason.Api";

    /// <summary>Cancelled when the client disconnects or after IMAGE_MASON_TIMEOUT_SECONDS.</summary>
    public CancellationTokenSource StartTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(Http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(Options.TimeoutSeconds));
        return timeout;
    }
}
