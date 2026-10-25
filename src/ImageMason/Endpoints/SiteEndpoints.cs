using System.Globalization;
using System.Text.Encodings.Web;
using ImageMason.Configuration;
using ImageMason.Storage;

namespace ImageMason.Endpoints;

internal static class SiteEndpoints
{
    /// <summary>Maps the landing page, the favicons and the stored images (GET, and DELETE with the API key).</summary>
    public static void MapSite(this WebApplication app, ImageMasonOptions options)
    {
        var landingPage = ReadText("index.html");

        app.MapGet("/", (HttpRequest request) =>
        {
            var html = landingPage
                .Replace("{{origin}}", HtmlEncoder.Default.Encode(ApiRequest.Origin(request, options.PublicBaseUrl)), StringComparison.Ordinal)
                .Replace("{{version}}", HtmlEncoder.Default.Encode(AppInfo.Version), StringComparison.Ordinal)
                .Replace("{{ttlMinutes}}", options.ImageTtlMinutes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        MapAsset(app, "/favicon.svg", "favicon.svg", "image/svg+xml");
        MapAsset(app, "/favicon.ico", "favicon.ico", "image/x-icon");
        MapAsset(app, "/apple-touch-icon.png", "apple-touch-icon.png", "image/png");

        // The random name is the access control; this route does not check the API key.
        app.MapGet("/images/{name}", (HttpContext http, string name, ImageStore store) =>
        {
            if (!store.TryGet(name, out var file))
                return Results.NotFound();

            http.Response.Headers.CacheControl = $"public, max-age={file.MaxAgeSeconds.ToString(CultureInfo.InvariantCulture)}";
            return Results.File(file.Path, file.ContentType);
        });

        // Removing an image before it expires needs the API key, unlike fetching it.
        app.MapDelete("/images/{name}", (HttpContext http, string name, ImageStore store, ILogger<ImageStore> logger) =>
        {
            if (!ApiRequest.IsAuthorized(http.Request, options.ApiKey))
                return ApiRequest.Unauthorized();

            if (!store.Delete(name))
                return ApiRequest.Problem(StatusCodes.Status404NotFound, "No stored image has that name.");

            logger.LogInformation("Deleted stored image {Name}", name);
            return Results.NoContent();
        });
    }

    private static void MapAsset(WebApplication app, string route, string name, string contentType)
    {
        var bytes = ReadBytes(name);
        app.MapGet(route, (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Bytes(bytes, contentType);
        });
    }

    private static string ReadText(string name)
    {
        using var reader = new StreamReader(Open(name));
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(string name)
    {
        using var stream = Open(name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // wwwroot files are embedded in the binary (see ImageMason.csproj), so the image needs no web root.
    private static Stream Open(string name) =>
        typeof(SiteEndpoints).Assembly.GetManifestResourceStream($"ImageMason.wwwroot.{name}")
            ?? throw new InvalidOperationException($"Embedded resource wwwroot/{name} is missing.");
}
