using System.Globalization;
using System.Text.Encodings.Web;
using ImageMason.Configuration;
using ImageMason.Storage;
using Microsoft.AspNetCore.StaticFiles;

namespace ImageMason.Endpoints;

internal static class SiteEndpoints
{
    private const string ResourcePrefix = "wwwroot/";

    // The web interface runs scripts, so its page only allows this server's own files (images also from blob:
    // and data: URLs, which the interface creates for results).
    private const string WebInterfacePolicy =
        "default-src 'self'; img-src 'self' blob: data:; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    // Embedded files by their path under wwwroot, such as "docs.html" or "app/assets/index-1a2b3c.js". The web
    // interface's build (src/web/dist) is embedded under app/ when it exists; see ImageMason.csproj.
    private static readonly Dictionary<string, string> Files = typeof(SiteEndpoints).Assembly.GetManifestResourceNames()
        .Select(name => (Name: name, Path: name.Replace('\\', '/')))
        .Where(resource => resource.Path.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .ToDictionary(resource => resource.Path[ResourcePrefix.Length..], resource => resource.Name, StringComparer.Ordinal);

    /// <summary>
    /// Maps the web interface (/ and /assets), the usage examples (/docs), the favicons and the stored images (GET,
    /// and DELETE with the API key).
    /// </summary>
    public static void MapSite(this WebApplication app, ImageMasonOptions options)
    {
        var webInterface = Files.ContainsKey("app/index.html") ? ReadBytes("app/index.html") : null;
        var notBuiltPage = ReadBytes("no-app.html");
        var docsPage = ReadText("docs.html")
            .Replace("{{webInterfaceLink}}", options.UiEnabled ? """<a href="/">Home</a>""" : "", StringComparison.Ordinal)
            .Replace("{{fontFaces}}", options.UiEnabled ? FontFaces() : "", StringComparison.Ordinal);
        var contentTypes = new FileExtensionContentTypeProvider();

        app.MapGet("/", (HttpContext http) =>
        {
            if (!options.UiEnabled)
                return Results.Redirect("/docs");

            http.Response.Headers.CacheControl = "no-cache";
            if (webInterface is null)
                return Results.Bytes(notBuiltPage, "text/html; charset=utf-8");

            http.Response.Headers.ContentSecurityPolicy = WebInterfacePolicy;
            return Results.Bytes(webInterface, "text/html; charset=utf-8");
        });

        // Vite's build files. Their names change with their contents, so browsers may keep them for good.
        app.MapGet("/assets/{**path}", (HttpContext http, string path) =>
        {
            var file = "app/assets/" + path;
            if (!options.UiEnabled || !Files.ContainsKey(file))
                return Results.NotFound();

            if (!contentTypes.TryGetContentType(path, out var contentType))
                contentType = "application/octet-stream";

            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Stream(Open(file), contentType);
        });

        app.MapGet("/docs", (HttpRequest request) =>
        {
            var html = docsPage
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
        app.MapDelete("/images/{name}", (string name, ImageStore store, ILogger<ImageStore> logger) =>
        {
            if (!store.Delete(name))
                return ApiRequest.Problem(StatusCodes.Status404NotFound, "No stored image has that name.");

            logger.LogInformation("Deleted stored image {Name}", name);
            return Results.NoContent();
        }).RequireApiKey();
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

    // @font-face rules for the web interface's Barlow fonts (Latin only), so /docs can match it. Vite names the files
    // by their contents, so each is found by its prefix. Missing files are skipped and the page falls back to the
    // system font.
    private static string FontFaces()
    {
        (string Family, string File, int Weight)[] faces =
        [
            ("Barlow", "barlow", 400),
            ("Barlow", "barlow", 500),
            ("Barlow", "barlow", 600),
            ("Barlow Semi Condensed", "barlow-semi-condensed", 600),
            ("Barlow Semi Condensed", "barlow-semi-condensed", 700),
        ];

        return string.Join("\n    ", faces
            .Select(face => (face, Path: Files.Keys.FirstOrDefault(path =>
                path.StartsWith($"app/assets/{face.File}-latin-{face.Weight}-normal-", StringComparison.Ordinal)
                && path.EndsWith(".woff2", StringComparison.Ordinal))))
            .Where(found => found.Path is not null)
            .Select(found => $$"""@font-face { font-family: "{{found.face.Family}}"; font-weight: {{found.face.Weight}}; font-display: swap; src: url("/{{found.Path!["app/".Length..]}}") format("woff2"); }"""));
    }

    private static string ReadText(string path)
    {
        using var reader = new StreamReader(Open(path));
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(string path)
    {
        using var stream = Open(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // wwwroot files are embedded in the binary (see ImageMason.csproj), so the image needs no web root.
    private static Stream Open(string path) =>
        (Files.TryGetValue(path, out var name) ? typeof(SiteEndpoints).Assembly.GetManifestResourceStream(name) : null)
            ?? throw new InvalidOperationException($"Embedded resource wwwroot/{path} is missing.");
}
