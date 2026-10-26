using System.Net;
using ImageMason.Configuration;
using ImageMason.Http;

namespace ImageMason.Tests.Http;

public sealed class UrlFetchPolicyTests
{
    private static string? Check(string url, string[]? hosts = null, string[]? extensions = null) =>
        new UrlFetchPolicy(new ImageMasonOptions
        {
            UrlAllowedHosts = hosts ?? [],
            UrlAllowedExtensions = extensions ?? [],
        }).Check(new Uri(url));

    [Theory]
    [InlineData("https://anything.example/photo")]
    [InlineData("http://10.0.0.5:8080/image.png")]
    public void NoRules_AllowsAnyHttpUrl(string url)
    {
        Assert.Null(Check(url));
    }

    [Fact]
    public void NonHttpScheme_IsRejected()
    {
        Assert.NotNull(Check("ftp://example.com/photo.jpg"));
        Assert.NotNull(Check("file:///etc/passwd"));
    }

    [Theory]
    [InlineData("https://cdn.example.com/a.jpg", true)]
    [InlineData("https://CDN.Example.com/a.jpg", true)]
    [InlineData("https://img.example.org/a.jpg", true)]
    [InlineData("https://a.b.example.org/a.jpg", true)]
    [InlineData("https://example.org/a.jpg", false)]
    [InlineData("https://badexample.org/a.jpg", false)]
    [InlineData("https://cdn.example.com.evil.net/a.jpg", false)]
    [InlineData("https://other.example.com/a.jpg", false)]
    public void HostRules_MatchExactHostsAndSubdomainWildcards(string url, bool allowed)
    {
        var reason = Check(url, hosts: ["cdn.example.com", "*.example.org"]);

        Assert.Equal(allowed, reason is null);
    }

    [Theory]
    [InlineData("https://example.com/photo.jpg", true)]
    [InlineData("https://example.com/photo.JPEG?size=large", true)]
    [InlineData("https://example.com/photo.gif", false)]
    [InlineData("https://example.com/photo", false)]
    [InlineData("https://example.com/photo.jpg/", false)]
    public void ExtensionRules_CheckThePathIgnoringTheQuery(string url, bool allowed)
    {
        var reason = Check(url, extensions: ["jpg", "jpeg", "png"]);

        Assert.Equal(allowed, reason is null);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.10", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("::", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("64:ff9b::a00:1", true)]
    [InlineData("64:ff9b::808:808", false)]
    [InlineData("2002:a00:1::", true)]
    [InlineData("2606:4700:4700::1111", false)]
    public void IsPrivate_CoversNonPublicRanges(string address, bool expected)
    {
        Assert.Equal(expected, UrlFetchPolicy.IsPrivate(IPAddress.Parse(address)));
    }
}
