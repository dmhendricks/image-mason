using System.Globalization;
using ImageMason.Imaging;

namespace ImageMason.Configuration;

/// <summary>
/// Service settings. <see cref="Load"/> reads them from <c>IMAGE_MASON_*</c> environment variables
/// (documented in .env.sample); properties without a variable are fixed tuning values.
/// </summary>
public sealed class ImageMasonOptions
{
    public const string Prefix = "IMAGE_MASON_";

    /// <summary>Required in the X-Api-Key header when set. Empty turns authentication off.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Origin used in stored-image URLs, without a trailing slash. Empty uses the scheme and host of each request.</summary>
    public string? PublicBaseUrl { get; init; }

    public long MaxInputBytes { get; init; } = 25L * 1024 * 1024;

    /// <summary>Largest accepted image (width x height), checked against the file header before anything is decoded.</summary>
    public long MaxPixels { get; init; } = 50_000_000;

    /// <summary>
    /// Jobs that run at once. Defaults to the CPU count (the container's CPU limit, when it has one): scoring and most
    /// encoders use one core, and the memory gate, not this limit, keeps large jobs from running out of memory.
    /// </summary>
    public int MaxConcurrentJobs { get; init; } = Environment.ProcessorCount;

    /// <summary>Requests that wait for a slot before the service answers 503.</summary>
    public int MaxQueuedJobs { get; init; } = 16;

    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Memory the service may use. Optimization jobs share it minus <see cref="ServiceReserveBytes"/>, each reserving
    /// its estimated peak before it starts. Null uses the container's memory limit, or the host's memory without one.
    /// </summary>
    public long? MemoryLimitBytes { get; init; }

    /// <summary>Memory kept back from jobs for the service itself.</summary>
    public const long ServiceReserveBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Requests that move an image without running tools (info, fetch, upload) at once. Each holds up to one input in
    /// memory outside the job budget, so together they get half of <see cref="ServiceReserveBytes"/>: 5 with the
    /// default 25 MB input limit, between 2 and 16 otherwise.
    /// </summary>
    public int MaxConcurrentTransfers => (int)Math.Clamp(ServiceReserveBytes / 2 / Math.Max(1, MaxInputBytes), 2, 16);

    /// <summary>
    /// Images displayed wider than this are downscaled. 0 turns default resizing off. The lossless preset only
    /// resizes when a request passes maxWidth.
    /// </summary>
    public int DefaultMaxWidth { get; init; }

    /// <summary>Preset for requests that send no quality: High, Medium or Low.</summary>
    public QualityPreset DefaultQuality { get; init; } = QualityPreset.Medium;

    // Minimum SSIMULACRA2 score per preset. Tiers from the SSIMULACRA2 README:
    // 90 = not noticeable in a flicker test, 85 = not noticeable in place at 1:1,
    // 80 = not noticeable side by side at 1:1, 70 = barely noticeable side by side.
    public double HighMinScore { get; init; } = 85;
    public double MediumMinScore { get; init; } = 80;
    public double LowMinScore { get; init; } = 70;

    /// <summary>Floor under the lossless preset when the result can't be lossless: a resized or converted JPEG.</summary>
    public double NearLosslessMinScore { get; init; } = 90;

    /// <summary>jpegli butteraugli distance range searched for the smallest result that meets the floor.</summary>
    public double MinJpegDistance { get; init; } = 0.5;
    public double MaxJpegDistance { get; init; } = 6.0;

    /// <summary>Distance change per attempt while bracketing the answer.</summary>
    public double SearchStep { get; init; } = 0.5;

    /// <summary>Stop bisecting once the passing and failing distances are this close.</summary>
    public double SearchPrecision { get; init; } = 0.25;

    /// <summary>Encode + score attempts per JPEG (the cap; most images need 3).</summary>
    public int MaxSearchSteps { get; init; } = 5;

    /// <summary>pngquant attempts per PNG, from gentlest to most aggressive, stopping at the first that fails.</summary>
    public int MaxPngQuantAttempts { get; init; } = 7;

    /// <summary>oxipng --opt level. "max" was never smaller than 4 in testing and took 2-3x longer.</summary>
    public string PngOptimizationLevel { get; init; } = "4";

    /// <summary>PNGs above this size use oxipng -o 2 (about half the time of -o 4, ~2% larger).</summary>
    public long PngFastAboveBytes { get; init; } = 1024 * 1024;

    /// <summary>oxipng --timeout: stops trying new filter/compression combinations after this.</summary>
    public int OxipngTimeoutSeconds { get; init; } = 20;

    /// <summary>Lowest cwebp and avifenc -q searched (0-100, higher is better quality).</summary>
    public double MinWebPQuality { get; init; } = 10;
    public double MinAvifQuality { get; init; } = 10;

    /// <summary>Quality change per attempt while bracketing the WebP and AVIF answer.</summary>
    public double QualitySearchStep { get; init; } = 5;

    /// <summary>cwebp -z for lossless WebP. 9 was no smaller than 6 in testing and took 5x longer.</summary>
    public int WebPLosslessEffort { get; init; } = 6;

    /// <summary>avifenc -s (0 slowest, 10 fastest); 6 is libavif's default.</summary>
    public int AvifSpeed { get; init; } = 6;

    /// <summary>cjxl butteraugli distance range searched for the smallest JPEG XL that meets the floor (0 is lossless).</summary>
    public double MinJxlDistance { get; init; } = 0.1;
    public double MaxJxlDistance { get; init; } = 6.0;

    /// <summary>cjxl -e (1 fastest, 10 slowest); 7 is libjxl's default.</summary>
    public int JxlEffort { get; init; } = 7;

    /// <summary>Directory for stored images, served at GET /images/{name}.</summary>
    public string StorePath { get; init; } = "/data/images";

    /// <summary>How long a stored image is served before cleanup deletes it.</summary>
    public int ImageTtlMinutes { get; init; } = 60;

    /// <summary>
    /// Soft cap on the total size of <see cref="StorePath"/>. When a new file would pass it, the oldest stored files
    /// are deleted until it fits; the new file is still saved. 0 turns the cap off.
    /// </summary>
    public long MaxStoreBytes { get; init; } = 10L * 1024 * 1024 * 1024;

    /// <summary>False rejects the url parameter, so only uploaded images are accepted.</summary>
    public bool UrlFetchEnabled { get; init; } = true;

    /// <summary>Hosts url may point to: <c>cdn.example.com</c> exactly, or <c>*.example.com</c> for subdomains. Empty allows any.</summary>
    public IReadOnlyList<string> UrlAllowedHosts { get; init; } = [];

    /// <summary>Extensions (without the dot) the url path must end in. Empty allows any.</summary>
    public IReadOnlyList<string> UrlAllowedExtensions { get; init; } = [];

    /// <summary>False turns the web interface off: / redirects to the usage examples at /docs. The API is unaffected.</summary>
    public bool UiEnabled { get; init; } = true;

    /// <summary>Minimum level for this service's own log messages.</summary>
    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    /// <exception cref="InvalidSettingException">A variable is set to a value that can't be used.</exception>
    public static ImageMasonOptions Load(IConfiguration configuration)
    {
        var defaults = new ImageMasonOptions();
        var settings = new Reader(configuration);

        return new ImageMasonOptions
        {
            ApiKey = settings.String("API_KEY"),
            PublicBaseUrl = settings.Origin("PUBLIC_BASE_URL"),
            MaxInputBytes = settings.Size("MAX_INPUT_SIZE", defaults.MaxInputBytes, allowZero: false),
            MaxPixels = settings.Long("MAX_PIXELS", defaults.MaxPixels, min: 1),
            MaxConcurrentJobs = settings.Int("MAX_CONCURRENT_JOBS", defaults.MaxConcurrentJobs, min: 1),
            MaxQueuedJobs = settings.Int("MAX_QUEUED_JOBS", defaults.MaxQueuedJobs, min: 0),
            TimeoutSeconds = settings.Int("TIMEOUT_SECONDS", defaults.TimeoutSeconds, min: 1, max: 86_400),
            MemoryLimitBytes = settings.String("MEMORY_LIMIT") is null ? null : settings.Size("MEMORY_LIMIT", 0, allowZero: false),
            DefaultMaxWidth = settings.Int("DEFAULT_MAX_WIDTH", defaults.DefaultMaxWidth, min: 0),
            DefaultQuality = settings.Quality("DEFAULT_QUALITY", defaults.DefaultQuality),
            HighMinScore = settings.Score("HIGH_MIN_SCORE", defaults.HighMinScore),
            MediumMinScore = settings.Score("MEDIUM_MIN_SCORE", defaults.MediumMinScore),
            LowMinScore = settings.Score("LOW_MIN_SCORE", defaults.LowMinScore),
            StorePath = Path.GetFullPath(settings.String("STORE_PATH") ?? defaults.StorePath),
            ImageTtlMinutes = settings.Int("IMAGE_TTL_MINUTES", defaults.ImageTtlMinutes, min: 1, max: 525_600),
            MaxStoreBytes = settings.Size("MAX_STORE_SIZE", defaults.MaxStoreBytes, allowZero: true),
            UrlFetchEnabled = settings.Bool("URL_FETCH_ENABLED", defaults.UrlFetchEnabled),
            UrlAllowedHosts = settings.Hosts("URL_ALLOWED_HOSTS"),
            UrlAllowedExtensions = settings.Extensions("URL_ALLOWED_EXTENSIONS"),
            UiEnabled = settings.Bool("UI_ENABLED", defaults.UiEnabled),
            LogLevel = settings.Level("LOG_LEVEL", defaults.LogLevel),
        };
    }

    private sealed class Reader(IConfiguration configuration)
    {
        public string? String(string name)
        {
            var value = configuration[Prefix + name];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public int Int(string name, int fallback, int min, int max = int.MaxValue) =>
            (int)Long(name, fallback, min, max);

        public long Long(string name, long fallback, long min, long max = long.MaxValue)
        {
            if (String(name) is not { } text)
                return fallback;

            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw Invalid(name, text, max is int.MaxValue or long.MaxValue
                    ? $"a whole number of at least {min}"
                    : $"a whole number from {min} to {max}");

            return value;
        }

        public double Score(string name, double fallback)
        {
            if (String(name) is not { } text)
                return fallback;

            if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 100)
                throw Invalid(name, text, "a number from 0 to 100");

            return value;
        }

        public long Size(string name, long fallback, bool allowZero)
        {
            if (String(name) is not { } text)
                return fallback;

            if (!ByteSizes.TryParse(text, out var bytes) || (bytes == 0 && !allowZero))
                throw Invalid(name, text, allowZero
                    ? "a byte count or a size such as 50m or 10g (0 turns it off)"
                    : "a byte count or a size such as 25m");

            return bytes;
        }

        public QualityPreset Quality(string name, QualityPreset fallback)
        {
            if (String(name) is not { } text)
                return fallback;

            return text.ToLowerInvariant() switch
            {
                "high" => QualityPreset.High,
                "medium" => QualityPreset.Medium,
                "low" => QualityPreset.Low,
                _ => throw Invalid(name, text, "high, medium or low"),
            };
        }

        public bool Bool(string name, bool fallback)
        {
            if (String(name) is not { } text)
                return fallback;

            return text.ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => true,
                "false" or "0" or "no" or "off" => false,
                _ => throw Invalid(name, text, "true or false"),
            };
        }

        public string? Origin(string name)
        {
            if (String(name) is not { } text)
                return null;

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw Invalid(name, text, "an absolute http or https URL such as https://images.example.com");

            return text.TrimEnd('/');
        }

        public string[] Hosts(string name)
        {
            var hosts = List(name);
            foreach (var host in hosts)
            {
                var bare = host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host;
                if (bare.Length == 0 || Uri.CheckHostName(bare) == UriHostNameType.Unknown)
                    throw Invalid(name, host, "host names such as cdn.example.com or *.example.com, separated by commas");
            }

            return hosts;
        }

        public string[] Extensions(string name) =>
            List(name).Select(extension => extension.TrimStart('*').TrimStart('.')).Where(extension => extension.Length > 0).ToArray();

        public LogLevel Level(string name, LogLevel fallback)
        {
            if (String(name) is not { } text)
                return fallback;

            if (!Enum.TryParse<LogLevel>(text, ignoreCase: true, out var level) || !Enum.IsDefined(level) || char.IsAsciiDigit(text[0]))
                throw Invalid(name, text, "Trace, Debug, Information, Warning, Error, Critical or None");

            return level;
        }

        private string[] List(string name) =>
            String(name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => item.ToLowerInvariant())
                .ToArray() ?? [];

        private static InvalidSettingException Invalid(string name, string value, string expected) =>
            new($"{Prefix}{name} is '{value}'; expected {expected}.");
    }
}

public sealed class InvalidSettingException(string message) : Exception(message);
