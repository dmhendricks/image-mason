using ImageMason.Configuration;
using ImageMason.Imaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ImageMason.Tests.Configuration;

public sealed class ImageMasonOptionsTests
{
    private static ImageMasonOptions Load(params (string Name, string Value)[] variables) =>
        ImageMasonOptions.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(variables.Select(v => KeyValuePair.Create<string, string?>(ImageMasonOptions.Prefix + v.Name, v.Value)))
            .Build());

    [Fact]
    public void Load_WithNothingSet_UsesDefaults()
    {
        var options = Load();
        var defaults = new ImageMasonOptions();

        Assert.Null(options.ApiKey);
        Assert.Equal(defaults.MaxInputBytes, options.MaxInputBytes);
        Assert.Equal(defaults.MaxPixels, options.MaxPixels);
        Assert.Equal(Environment.ProcessorCount, options.MaxConcurrentJobs);
        Assert.Equal(QualityPreset.High, options.DefaultQuality);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Null(options.MemoryLimitBytes);
        Assert.True(options.UrlFetchEnabled);
        Assert.True(options.UrlAllowPrivate);
        Assert.Empty(options.UrlAllowedHosts);
        Assert.True(options.UiEnabled);
        Assert.Equal(LogLevel.Information, options.LogLevel);
    }

    [Fact]
    public void Load_ReadsEveryVariable()
    {
        var options = Load(
            ("API_KEY", " secret "),
            ("PUBLIC_BASE_URL", "https://images.example.com/"),
            ("MAX_INPUT_SIZE", "10m"),
            ("MAX_PIXELS", "24000000"),
            ("MAX_CONCURRENT_JOBS", "4"),
            ("MAX_QUEUED_JOBS", "0"),
            ("TIMEOUT_SECONDS", "30"),
            ("MEMORY_LIMIT", "4g"),
            ("DEFAULT_QUALITY", "Low"),
            ("HIGH_MIN_SCORE", "88.5"),
            ("STORE_PATH", "/tmp/store"),
            ("IMAGE_TTL_MINUTES", "5"),
            ("MAX_STORE_SIZE", "0"),
            ("URL_FETCH_ENABLED", "false"),
            ("URL_ALLOW_PRIVATE", "false"),
            ("URL_ALLOWED_HOSTS", "CDN.example.com, *.example.org"),
            ("URL_ALLOWED_EXTENSIONS", ".PNG,*.jpg, jpeg"),
            ("UI_ENABLED", "false"),
            ("LOG_LEVEL", "debug"));

        Assert.Equal("secret", options.ApiKey);
        Assert.Equal("https://images.example.com", options.PublicBaseUrl);
        Assert.Equal(10L * 1024 * 1024, options.MaxInputBytes);
        Assert.Equal(24_000_000, options.MaxPixels);
        Assert.Equal(4, options.MaxConcurrentJobs);
        Assert.Equal(0, options.MaxQueuedJobs);
        Assert.Equal(30, options.TimeoutSeconds);
        Assert.Equal(4L * 1024 * 1024 * 1024, options.MemoryLimitBytes);
        Assert.Equal(QualityPreset.Low, options.DefaultQuality);
        Assert.Equal(88.5, options.HighMinScore);
        Assert.Equal("/tmp/store", options.StorePath);
        Assert.Equal(5, options.ImageTtlMinutes);
        Assert.Equal(0, options.MaxStoreBytes);
        Assert.False(options.UrlFetchEnabled);
        Assert.False(options.UrlAllowPrivate);
        Assert.Equal(["cdn.example.com", "*.example.org"], options.UrlAllowedHosts);
        Assert.Equal(["png", "jpg", "jpeg"], options.UrlAllowedExtensions);
        Assert.False(options.UiEnabled);
        Assert.Equal(LogLevel.Debug, options.LogLevel);
    }

    [Theory]
    [InlineData("MAX_PIXELS", "lots")]
    [InlineData("MAX_PIXELS", "0")]
    [InlineData("MAX_CONCURRENT_JOBS", "-1")]
    [InlineData("TIMEOUT_SECONDS", "86401")]
    [InlineData("IMAGE_TTL_MINUTES", "525601")]
    [InlineData("MAX_INPUT_SIZE", "0")]
    [InlineData("MEMORY_LIMIT", "0")]
    [InlineData("MEMORY_LIMIT", "plenty")]
    [InlineData("MAX_STORE_SIZE", "huge")]
    [InlineData("HIGH_MIN_SCORE", "101")]
    [InlineData("DEFAULT_QUALITY", "lossless")]
    [InlineData("DEFAULT_QUALITY", "best")]
    [InlineData("URL_FETCH_ENABLED", "maybe")]
    [InlineData("URL_ALLOWED_HOSTS", "cdn.example.com, not a host")]
    [InlineData("PUBLIC_BASE_URL", "images.example.com")]
    [InlineData("PUBLIC_BASE_URL", "ftp://images.example.com")]
    [InlineData("LOG_LEVEL", "loud")]
    [InlineData("LOG_LEVEL", "2")]
    public void Load_RejectsInvalidValues_NamingTheVariable(string name, string value)
    {
        var error = Assert.Throws<InvalidSettingException>(() => Load((name, value)));

        Assert.StartsWith(ImageMasonOptions.Prefix + name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MakesARelativeStorePathAbsolute()
    {
        Assert.Equal(Path.GetFullPath("store"), Load(("STORE_PATH", "store")).StorePath);
    }

    [Theory]
    [InlineData(25L * 1024 * 1024, 5)]
    [InlineData(100L * 1024 * 1024, 2)]
    [InlineData(1024 * 1024, 16)]
    public void MaxConcurrentTransfers_FitsHalfTheServiceReserve(long maxInputBytes, int expected)
    {
        Assert.Equal(expected, new ImageMasonOptions { MaxInputBytes = maxInputBytes }.MaxConcurrentTransfers);
    }
}
