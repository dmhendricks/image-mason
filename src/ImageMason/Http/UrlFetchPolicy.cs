using ImageMason.Configuration;

namespace ImageMason.Http;

/// <summary>Decides which URLs the url parameter may fetch, from the URL_FETCH_* and URL_ALLOWED_* settings.</summary>
public sealed class UrlFetchPolicy(ImageMasonOptions options)
{
    public bool Enabled => options.UrlFetchEnabled;

    /// <summary>Returns why <paramref name="uri"/> may not be fetched, or null when it may.</summary>
    public string? Check(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
            return "url must be an absolute http or https URL.";

        var host = uri.IdnHost.ToLowerInvariant();
        if (options.UrlAllowedHosts.Count > 0 && !options.UrlAllowedHosts.Any(pattern => HostMatches(host, pattern)))
            return $"Fetching images from {host} is not allowed.";

        if (options.UrlAllowedExtensions.Count > 0)
        {
            var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
            if (!options.UrlAllowedExtensions.Contains(extension))
                return $"url must end in .{string.Join(", .", options.UrlAllowedExtensions)}.";
        }

        return null;
    }

    /// <summary><c>*.example.com</c> matches any subdomain of example.com, but not example.com itself.</summary>
    internal static bool HostMatches(string host, string pattern) =>
        pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.Length > pattern.Length - 1 && host.EndsWith(pattern[1..], StringComparison.Ordinal)
            : host == pattern;
}
