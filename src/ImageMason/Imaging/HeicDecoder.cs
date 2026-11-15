using ImageMason.Configuration;

namespace ImageMason.Imaging;

/// <summary>Where HEIC support stands; see <see cref="HeicDecoder"/>.</summary>
public enum HeicStatus
{
    /// <summary>IMAGE_MASON_HEIC_ENABLED is off.</summary>
    Disabled,

    /// <summary>The decoder is being downloaded and checked; HEIC requests get 503 until it's done.</summary>
    Installing,

    Ready,

    /// <summary>The decoder couldn't be installed; the log says why.</summary>
    Failed
}

/// <summary>HEIC is turned on, but its decoder is still being installed. Answered with 503 and Retry-After.</summary>
public sealed class HeicNotReadyException(string message) : Exception(message);

/// <summary>
/// HEIC input. HEIC images are HEVC-coded, and HEVC decoders are patent-licensed, so the published image contains
/// none. With IMAGE_MASON_HEIC_ENABLED on, <see cref="InstallAsync"/> downloads Alpine's heif-dec, libheif and its
/// libde265 plugin at startup, checked against the Alpine keys in the image, and HEIC images are decoded with that
/// heif-dec. Every start checks Alpine's package index and downloads the packages only when there's a newer version,
/// so a restart picks up Alpine's security fixes; when Alpine's repositories can't be reached, the copy from the last
/// start is used.
/// </summary>
public sealed class HeicDecoder
{
    /// <summary>heif-dec, libheif, its HEVC plugin and the HEVC decoder. libheif's other plugins (AV1, JPEG) and its
    /// encoders aren't needed: AVIF has its own tools, and HEIC is never written.</summary>
    internal static readonly string[] PackageNames = ["libde265", "libheif", "libheif-libde265", "libheif-tools"];

    // apk gives up after about 10 s without a network; this only bounds a download that stalls.
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(2);

    private readonly ImageMasonOptions _options;
    private readonly ToolRunner _tools;
    private readonly ILogger _logger;
    private readonly string _apk;
    private readonly string _alpineRelease;
    private readonly string _root;
    private readonly string _versions;
    private volatile HeicStatus _status;

    public HeicDecoder(ImageMasonOptions options, ToolRunner tools, ILogger<HeicDecoder> logger)
        : this(options, tools, logger, "apk", "/etc/alpine-release")
    {
    }

    /// <param name="apk">The apk command; tests pass a stand-in.</param>
    /// <param name="alpineRelease">The file holding the image's Alpine release. A copy from the last start is only used
    /// on the same release, since its libraries were built against it.</param>
    internal HeicDecoder(ImageMasonOptions options, ToolRunner tools, ILogger logger, string apk, string alpineRelease)
    {
        _options = options;
        _tools = tools;
        _logger = logger;
        _apk = apk;
        _alpineRelease = alpineRelease;
        _status = options.HeicEnabled ? HeicStatus.Installing : HeicStatus.Disabled;

        // The packages unpack into root as they would into /: usr/bin, usr/lib and the plugins in usr/lib/libheif.
        _root = Path.Combine(options.HeicPath, "root");
        _versions = Path.Combine(options.HeicPath, "versions.txt");
        DecoderPath = Path.Combine(_root, "usr", "bin", "heif-dec");
        Environment = new Dictionary<string, string>
        {
            ["LD_LIBRARY_PATH"] = Path.Combine(_root, "usr", "lib"),
            ["LIBHEIF_PLUGIN_PATH"] = Path.Combine(_root, "usr", "lib", "libheif"),
        };
    }

    public HeicStatus Status => _status;

    /// <summary>The packages in use, with their versions (such as libheif-1.23.7-r0), once <see cref="HeicStatus.Ready"/>.</summary>
    public IReadOnlyList<string> Installed { get; private set; } = [];

    /// <summary>heif-dec, run with <see cref="Environment"/> so it finds its libraries and plugin.</summary>
    internal string DecoderPath { get; }

    internal IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>"JPEG, PNG, WebP, AVIF or JPEG XL", with HEIC when it's read: for messages about files that aren't.</summary>
    public string InputNames => _status == HeicStatus.Ready
        ? "JPEG, PNG, WebP, AVIF, JPEG XL or HEIC"
        : ImageFormatDetector.SupportedNames;

    /// <summary>Throws unless HEIC images can be decoded now.</summary>
    /// <exception cref="UnsupportedImageException">HEIC is off, or its decoder couldn't be installed.</exception>
    /// <exception cref="HeicNotReadyException">The decoder is still being installed.</exception>
    public void EnsureReady()
    {
        var others = $"Send a {ImageFormatDetector.SupportedNames} image instead.";
        switch (_status)
        {
            case HeicStatus.Ready:
                return;
            case HeicStatus.Installing:
                throw new HeicNotReadyException("This server is still setting up HEIC support. Retry in a few seconds.");
            case HeicStatus.Failed:
                throw new UnsupportedImageException($"This server couldn't set up HEIC support; its log says why. {others}");
            default:
                throw new UnsupportedImageException(
                    $"This server doesn't read HEIC images ({ImageMasonOptions.Prefix}HEIC_ENABLED is off). {others}");
        }
    }

    /// <summary>
    /// Downloads and checks the decoder, then sets <see cref="Status"/>. Throws only when cancelled: whatever goes wrong,
    /// the service runs without HEIC.
    /// </summary>
    public async Task InstallAsync(CancellationToken ct)
    {
        if (_status == HeicStatus.Disabled)
            return;

        _logger.LogInformation("HEIC input is on ({Setting}). Image Mason doesn't include an HEVC decoder: libheif and libde265 "
            + "are downloaded from Alpine's repositories. HEVC is covered by patents; whether you need a license to decode it "
            + "is your responsibility", ImageMasonOptions.Prefix + "HEIC_ENABLED");

        try
        {
            if (await DownloadAsync(ct) is not { } source || !await CheckAsync(ct))
            {
                _status = HeicStatus.Failed;
                return;
            }

            _status = HeicStatus.Ready;
            _logger.LogInformation("HEIC input is ready: {Packages} ({Source})", string.Join(", ", Installed), source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "HEIC input is off: the decoder couldn't be installed in {Path}", _options.HeicPath);
            _status = HeicStatus.Failed;
        }
    }

    /// <summary>
    /// Checks the package index, and fetches and unpacks the packages unless the same versions are unpacked already.
    /// Returns how the decoder was found, for the log, or null when there is none to use.
    /// </summary>
    private async Task<string?> DownloadAsync(CancellationToken ct)
    {
        if (!File.Exists(_alpineRelease))
        {
            _logger.LogError("HEIC input is off: it downloads Alpine packages, so it only works in the Image Mason container "
                + "({Path} is missing)", _alpineRelease);
            return null;
        }

        var release = $"alpine {(await File.ReadAllTextAsync(_alpineRelease, ct)).Trim()}\n";
        Directory.CreateDirectory(_options.HeicPath);
        var current = File.Exists(_versions) ? await File.ReadAllTextAsync(_versions, ct) : null;

        // The last start's copy, when the repositories can't be reached or a package won't unpack.
        string? Reuse(string problem, ToolResult result)
        {
            if (current is not null && current.StartsWith(release, StringComparison.Ordinal) && File.Exists(DecoderPath))
            {
                _logger.LogWarning("Couldn't update the HEIC decoder: {Problem} (apk exit {ExitCode}: {Error}). Using the copy "
                    + "from the last start", problem, result.ExitCode, result.StdErr.Trim());
                Installed = PackagesIn(current);
                return "kept from the last start";
            }

            _logger.LogError("HEIC input is off: {Problem} (apk exit {ExitCode}: {Error})", problem, result.ExitCode, result.StdErr.Trim());
            return null;
        }

        var download = Directory.CreateTempSubdirectory("image-mason-heic-");
        try
        {
            var packages = download.CreateSubdirectory("packages").FullName;
            var cache = download.CreateSubdirectory("cache").FullName;

            // Like apt, apk knows the current versions once it has the index (about 3 MB, in the cache folder).
            var search = await RunApkAsync(["search", "--cache-dir", cache, "--exact", .. PackageNames], ct);
            var latest = search.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Order(StringComparer.Ordinal).ToArray();
            if (search.ExitCode != 0 || latest.Length != PackageNames.Length)
                return Reuse("couldn't read Alpine's package index", search);

            var versions = release + string.Concat(latest.Select(package => package + "\n"));
            if (versions == current && File.Exists(DecoderPath))
            {
                Installed = PackagesIn(versions);
                return "up to date";
            }

            var fetch = await RunApkAsync(["fetch", "--cache-dir", cache, "--output", packages, .. PackageNames], ct);
            var files = Directory.GetFiles(packages, "*.apk").Order(StringComparer.Ordinal).ToArray();
            if (fetch.ExitCode != 0 || files.Length != PackageNames.Length)
                return Reuse("couldn't download it from Alpine's repositories", fetch);

            // Unpacked next to the copy in use and then swapped in, so a failure never leaves half a decoder.
            var staging = Path.Combine(_options.HeicPath, "root.new");
            DeleteDirectory(staging);
            Directory.CreateDirectory(staging);
            foreach (var file in files)
            {
                // apk checks each package against the index fetched above, which is signed with the image's Alpine keys.
                var extract = await _tools.RunAsync(_apk, ["extract", "--no-chown", "--cache-dir", cache, "--destination", staging, file], ct);
                if (extract.ExitCode != 0)
                {
                    DeleteDirectory(staging);
                    return Reuse($"{Path.GetFileName(file)} failed its check or couldn't be unpacked", extract);
                }
            }

            var previous = Path.Combine(_options.HeicPath, "root.old");
            DeleteDirectory(previous);
            if (Directory.Exists(_root))
                Directory.Move(_root, previous);
            Directory.Move(staging, _root);
            DeleteDirectory(previous);
            await File.WriteAllTextAsync(_versions, versions, ct);

            Installed = PackagesIn(versions);
            return current is null ? "downloaded" : "updated";
        }
        finally
        {
            DeleteDirectory(download.FullName);
        }
    }

    /// <summary>Runs apk against Alpine's repositories, giving up on a download that stalls.</summary>
    private async Task<ToolResult> RunApkAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(FetchTimeout);
        try
        {
            return await _tools.RunAsync(_apk, args, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ToolResult(-1, "", $"no answer within {FetchTimeout.TotalSeconds} s", FetchTimeout);
        }
    }

    /// <summary>Runs heif-dec once, to be sure it starts and has its HEVC plugin.</summary>
    private async Task<bool> CheckAsync(CancellationToken ct)
    {
        var result = await _tools.RunAsync(DecoderPath, ["--list-decoders"], ct, Environment);
        if (result.ExitCode == 0 && result.StdOut.Contains("libde265", StringComparison.Ordinal))
            return true;

        _logger.LogError("HEIC input is off: heif-dec has no HEVC decoder (exit {ExitCode}): {Output}", result.ExitCode,
            (result.StdOut + result.StdErr).Trim());
        return false;
    }

    // versions.txt: the Alpine release, then one package per line.
    private static string[] PackagesIn(string versions) =>
        versions.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1..];

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}

/// <summary>Installs the HEIC decoder in the background, so the service answers requests while it downloads.</summary>
internal sealed class HeicInstallService(HeicDecoder decoder) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => decoder.InstallAsync(stoppingToken);
}
