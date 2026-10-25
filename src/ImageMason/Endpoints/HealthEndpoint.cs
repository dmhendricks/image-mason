using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ImageMason.Endpoints;

internal static class HealthEndpoint
{
    /// <summary>GET /health: status and build information, used by the container HEALTHCHECK.</summary>
    public static void MapHealth(this WebApplication app)
    {
        var build = new BuildInfo(
            Version: AppInfo.Version,
            Date: ReadBuildDate(),
            DotNet: Environment.Version.ToString(),
            Arch: Architecture());
        var startedAt = new DateTimeOffset(Process.GetCurrentProcess().StartTime);

        app.MapGet("/health", () =>
        {
            var now = DateTimeOffset.Now;
            return Results.Ok(new HealthResponse(
                Status: "healthy",
                Uptime: FormatUptime(now - startedAt),
                Timestamp: RoundToSecond(now).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                Build: build));
        });
    }

    // Docker's platform names, so the value matches the image tag's platform (linux/amd64, linux/arm64).
    private static string Architecture() => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "amd64",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        var other => other.ToString().ToLowerInvariant(),
    };

    // Written by the image build. Shown in the container timezone so it lines up with timestamp.
    private static string ReadBuildDate()
    {
        const string path = "/app/build-date";
        if (!File.Exists(path))
            return "";

        var text = File.ReadAllText(path).Trim();
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var built))
            return text;

        return built.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static string FormatUptime(TimeSpan elapsed)
    {
        var seconds = Math.Max(0, (long)Math.Round(elapsed.TotalSeconds, MidpointRounding.AwayFromZero));

        var days = seconds / 86_400;
        seconds %= 86_400;
        var hours = seconds / 3_600;
        seconds %= 3_600;
        var minutes = seconds / 60;
        seconds %= 60;

        if (days > 0)
            return $"{days}d {hours}h {minutes}m {seconds}s";
        if (hours > 0)
            return $"{hours}h {minutes}m {seconds}s";
        if (minutes > 0)
            return $"{minutes}m {seconds}s";
        return $"{seconds}s";
    }

    private static DateTimeOffset RoundToSecond(DateTimeOffset value)
    {
        var rounded = (value.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond;
        return new DateTimeOffset(rounded, value.Offset);
    }
}
