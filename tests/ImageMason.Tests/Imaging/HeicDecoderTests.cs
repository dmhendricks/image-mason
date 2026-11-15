using System.Runtime.Versioning;
using ImageMason.Configuration;
using ImageMason.Imaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageMason.Tests.Imaging;

/// <summary>The decoder's install, with a stand-in apk that "downloads" empty packages and unpacks a stand-in heif-dec.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class HeicDecoderTests : IDisposable
{
    // search prints each package at the version in ./version, and fetch writes one file for each, both failing when
    // ./offline exists; extract copies the package into the destination, adds heif-dec for libheif-tools, and fails
    // when ./corrupt exists. Calls go to ./calls.
    private const string FakeApk = """
        #!/bin/sh
        dir=$(dirname "$0")
        echo "$*" >> "$dir/calls"
        command=$1
        shift
        case "$command" in
        search)
          [ -e "$dir/offline" ] && { echo "unable to read the index" >&2; exit 1; }
          for arg in "$@"; do
            case "$arg" in
              --cache-dir|--exact) ;;
              /*) ;;
              *) echo "$arg-$(cat "$dir/version")" ;;
            esac
          done ;;
        fetch)
          [ -e "$dir/offline" ] && { echo "unable to select packages" >&2; exit 4; }
          while [ $# -gt 0 ]; do
            case "$1" in
              --cache-dir) shift 2 ;;
              --output) out=$2; shift 2 ;;
              *) echo "$1" > "$out/$1-$(cat "$dir/version").apk"; shift ;;
            esac
          done ;;
        extract)
          while [ $# -gt 0 ]; do
            case "$1" in
              --no-chown) shift ;;
              --cache-dir) shift 2 ;;
              --destination) dest=$2; shift 2 ;;
              *) file=$1; shift ;;
            esac
          done
          [ -e "$dir/corrupt" ] && { echo "file format is invalid or inconsistent" >&2; exit 99; }
          mkdir -p "$dest/usr/lib/libheif"
          cp "$file" "$dest/usr/lib/"
          case $(basename "$file") in
            libheif-tools-*) mkdir -p "$dest/usr/bin" && cp "$dir/heif-dec" "$dest/usr/bin/heif-dec" ;;
          esac ;;
        esac
        """;

    private const string FakeHeifDec = """
        #!/bin/sh
        echo "HEIC decoders:"
        echo "- libde265 = libde265 HEVC decoder, version 1.1.3"
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("image-mason-heic-test-").FullName;
    private readonly ToolRunner _tools = new(NullLogger<ToolRunner>.Instance);

    public HeicDecoderTests()
    {
        File.WriteAllText(Path.Combine(_dir, "alpine-release"), "3.24.4\n");
        File.WriteAllText(Path.Combine(_dir, "version"), "1.0-r0");
        WriteScript("apk", FakeApk);
        WriteScript("heif-dec", FakeHeifDec);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string HeicPath => Path.Combine(_dir, "heic");

    private HeicDecoder Decoder(bool enabled = true, string alpineRelease = "alpine-release") =>
        new(new ImageMasonOptions { HeicEnabled = enabled, HeicPath = HeicPath }, _tools, NullLogger.Instance,
            Path.Combine(_dir, "apk"), Path.Combine(_dir, alpineRelease));

    private void WriteScript(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text.Replace("\r\n", "\n") + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private void Touch(string name) => File.WriteAllText(Path.Combine(_dir, name), "");

    private int Calls(string command) =>
        File.Exists(Path.Combine(_dir, "calls")) ? File.ReadAllLines(Path.Combine(_dir, "calls")).Count(line => line.StartsWith(command)) : 0;

    [Fact]
    public async Task Disabled_InstallsNothing_AndRefusesHeic()
    {
        var decoder = Decoder(enabled: false);

        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Disabled, decoder.Status);
        Assert.False(Directory.Exists(HeicPath));
        var error = Assert.Throws<UnsupportedImageException>(decoder.EnsureReady);
        Assert.Contains("IMAGE_MASON_HEIC_ENABLED is off", error.Message);
        Assert.Equal(ImageFormatDetector.SupportedNames, decoder.InputNames);
    }

    [Fact]
    public void BeforeTheInstall_HeicIsRetried()
    {
        var decoder = Decoder();

        Assert.Equal(HeicStatus.Installing, decoder.Status);
        Assert.Throws<HeicNotReadyException>(decoder.EnsureReady);
    }

    [Fact]
    public async Task Install_DownloadsUnpacksAndChecks()
    {
        var decoder = Decoder();

        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Ready, decoder.Status);
        decoder.EnsureReady();
        Assert.Equal(["libde265-1.0-r0", "libheif-1.0-r0", "libheif-libde265-1.0-r0", "libheif-tools-1.0-r0"], decoder.Installed);
        Assert.True(File.Exists(decoder.DecoderPath));
        Assert.EndsWith("HEIC", decoder.InputNames);
        Assert.False(Directory.Exists(Path.Combine(HeicPath, "root.new")));
    }

    [Fact]
    public async Task NextStart_WithTheSameVersions_DownloadsNoPackages()
    {
        await Decoder().InstallAsync(Ct);
        var (fetches, extracts) = (Calls("fetch"), Calls("extract"));

        var decoder = Decoder();
        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Ready, decoder.Status);
        Assert.Equal(2, Calls("search"));
        Assert.Equal((fetches, extracts), (Calls("fetch"), Calls("extract")));
    }

    [Fact]
    public async Task NextStart_WithNewVersions_ReplacesTheCopy()
    {
        await Decoder().InstallAsync(Ct);
        File.WriteAllText(Path.Combine(_dir, "version"), "1.1-r0");

        var decoder = Decoder();
        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Ready, decoder.Status);
        Assert.Contains("libheif-1.1-r0", decoder.Installed);
        var unpacked = Directory.GetFiles(Path.Combine(HeicPath, "root", "usr", "lib"), "*.apk").Select(Path.GetFileName);
        Assert.All(unpacked, name => Assert.Contains("-1.1-r0", name));
        Assert.False(Directory.Exists(Path.Combine(HeicPath, "root.old")));
    }

    [Fact]
    public async Task Offline_UsesTheCopyFromTheLastStart()
    {
        await Decoder().InstallAsync(Ct);
        Touch("offline");

        var decoder = Decoder();
        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Ready, decoder.Status);
        Assert.Contains("libheif-1.0-r0", decoder.Installed);
    }

    [Fact]
    public async Task Offline_WithoutACopy_Fails()
    {
        Touch("offline");
        var decoder = Decoder();

        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Failed, decoder.Status);
        Assert.Contains("couldn't set up HEIC", Assert.Throws<UnsupportedImageException>(decoder.EnsureReady).Message);
    }

    [Fact]
    public async Task Offline_WithACopyForAnotherAlpineRelease_Fails()
    {
        await Decoder().InstallAsync(Ct);
        Touch("offline");
        File.WriteAllText(Path.Combine(_dir, "alpine-release"), "3.25.0\n");

        var decoder = Decoder();
        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Failed, decoder.Status);
    }

    [Fact]
    public async Task APackageThatFailsItsCheck_LeavesNothingHalfUnpacked()
    {
        Touch("corrupt");
        var decoder = Decoder();

        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Failed, decoder.Status);
        Assert.False(Directory.Exists(Path.Combine(HeicPath, "root")));
        Assert.False(Directory.Exists(Path.Combine(HeicPath, "root.new")));
    }

    [Fact]
    public async Task ADecoderWithoutItsHevcPlugin_Fails()
    {
        WriteScript("heif-dec", "#!/bin/sh\necho 'HEIC decoders:'");
        var decoder = Decoder();

        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Failed, decoder.Status);
    }

    [Fact]
    public async Task OutsideTheContainer_Fails()
    {
        var decoder = Decoder(alpineRelease: "missing");

        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Failed, decoder.Status);
    }

    [Fact]
    public async Task WithoutApk_Fails_InsteadOfThrowing()
    {
        File.Delete(Path.Combine(_dir, "apk"));
        var decoder = Decoder();

        await decoder.InstallAsync(Ct);

        Assert.Equal(HeicStatus.Failed, decoder.Status);
    }
}
