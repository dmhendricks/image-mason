using System.Net;
using System.Net.Sockets;
using ImageMason.Configuration;

namespace ImageMason.Http;

/// <summary>Decides which URLs the url parameter may fetch, from the URL_FETCH_* and URL_ALLOWED_* settings.</summary>
public sealed class UrlFetchPolicy(ImageMasonOptions options)
{
    public bool Enabled => options.UrlFetchEnabled;

    public bool AllowPrivate => options.UrlAllowPrivate;

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

    /// <summary>
    /// True for addresses that aren't on the public internet: loopback, private (RFC 1918, unique local), link-local
    /// (including the 169.254.169.254 cloud metadata address), shared (100.64/10), multicast, reserved and unspecified,
    /// also when wrapped in IPv6 (mapped, NAT64 or 6to4).
    /// </summary>
    internal static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => true,
                100 => bytes[1] is >= 64 and < 128,
                169 => bytes[1] == 254,
                172 => bytes[1] is >= 16 and < 32,
                192 => bytes[1] == 168 || (bytes[1] == 0 && bytes[2] is 0 or 2),
                198 => bytes[1] is 18 or 19 || (bytes[1] == 51 && bytes[2] == 100),
                203 => bytes[1] == 0 && bytes[2] == 113,
                >= 224 => true,
                _ => false
            };
        }

        // NAT64 (64:ff9b::/96) and 6to4 (2002::/16) carry an IPv4 address that decides.
        if (bytes.AsSpan(0, 12).SequenceEqual((ReadOnlySpan<byte>)[0, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0]))
            return IsPrivate(new IPAddress(bytes.AsSpan(12, 4)));
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
            return IsPrivate(new IPAddress(bytes.AsSpan(2, 4)));

        // The first 96 bits zero: unspecified (::), loopback (::1) and the old IPv4-compatible form.
        return bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast
            || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8); // 2001:db8::/32, documentation
    }

    /// <summary><c>*.example.com</c> matches any subdomain of example.com, but not example.com itself.</summary>
    internal static bool HostMatches(string host, string pattern) =>
        pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.Length > pattern.Length - 1 && host.EndsWith(pattern[1..], StringComparison.Ordinal)
            : host == pattern;
}
