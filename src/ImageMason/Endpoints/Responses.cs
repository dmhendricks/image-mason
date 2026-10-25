using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMason.Imaging;
using Microsoft.AspNetCore.Mvc;

namespace ImageMason.Endpoints;

/// <summary>Returned by optimize?output=json and upload. Null properties are omitted.</summary>
public sealed class HostedImageResponse
{
    /// <summary>URL of the stored result.</summary>
    public required string Output { get; init; }

    /// <summary>URL of the stored input. Only with keepOriginal=true.</summary>
    public string? Input { get; init; }

    public required string MimeType { get; init; }
    public required long OriginalSize { get; init; }
    public required long CompressedSize { get; init; }

    /// <summary>Percent smaller than the original. Negative when the result is larger.</summary>
    public required double ReductionPercent { get; init; }

    public required int OriginalWidth { get; init; }
    public required int OriginalHeight { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string Quality { get; init; }
    public required string Method { get; init; }

    /// <summary>SSIMULACRA2 score. Only when the result was scored.</summary>
    public double? Score { get; init; }

    /// <summary>Score floor the result had to meet. Only when there was one.</summary>
    public double? MinScore { get; init; }

    /// <summary>Whether the input PNG looks like a photo with no transparency. Only for PNG inputs.</summary>
    public bool? IsPhoto { get; init; }

    /// <summary>When the URLs stop being served, in the container timezone (yyyy-MM-ddTHH:mm:ss±hh:mm).</summary>
    public required string ExpiresAt { get; init; }

    public required long DurationMs { get; init; }
}

/// <summary>Returned by info. Read from the file header; nothing is decoded. Null properties are omitted.</summary>
public sealed class ImageInfoResponse
{
    /// <summary>jpeg, png, webp or avif.</summary>
    public required string Format { get; init; }

    public required string MimeType { get; init; }
    public required long Size { get; init; }

    /// <summary>Stored pixel dimensions.</summary>
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>1 is upright; 2-8 are the EXIF orientations a viewer applies (AVIF rotation is reported as 6).</summary>
    public required int Orientation { get; init; }

    /// <summary>Dimensions as displayed, after the orientation is applied.</summary>
    public required int DisplayWidth { get; init; }

    public required int DisplayHeight { get; init; }
    public required double Megapixels { get; init; }
    public required int BitDepth { get; init; }
    public required bool HasAlpha { get; init; }
    public required bool HasIccProfile { get; init; }
    public required bool Animated { get; init; }

    /// <summary>Whether the PNG looks like a photo with no transparency. Only for PNG.</summary>
    public bool? IsPhoto { get; init; }

    /// <summary>The image is within IMAGE_MASON_MAX_PIXELS, so optimize and convert accept it.</summary>
    public required bool WithinPixelLimit { get; init; }

    /// <summary>Camera, date, exposure, author and GPS location from EXIF. Only when present (not read for AVIF yet).</summary>
    public ExifSummary? Exif { get; init; }
}

/// <summary>Returned by limits: what the server accepts, so clients can check an image before sending it.</summary>
/// <param name="MaxInputSize">Largest accepted file, in bytes (IMAGE_MASON_MAX_INPUT_SIZE).</param>
/// <param name="MaxPixels">Largest accepted image, and largest result, in pixels (IMAGE_MASON_MAX_PIXELS).</param>
/// <param name="Presets">Each quality preset's minimum SSIMULACRA2 score; null for lossless.</param>
/// <param name="DefaultMaxWidth">Width optimize and convert downscale to when a request sends no maxWidth; 0 is off.</param>
/// <param name="ImageTtlMinutes">How long stored images are served.</param>
/// <param name="TimeoutSeconds">How long a request may take before 504.</param>
/// <param name="UrlFetch">Whether images can be fetched with the url parameter.</param>
/// <param name="ApiKeyRequired">Whether /api requests need the X-Api-Key header.</param>
public sealed record LimitsResponse(
    IReadOnlyList<FormatLimits> Formats,
    long MaxInputSize,
    long MaxPixels,
    IReadOnlyDictionary<string, double?> Presets,
    int DefaultMaxWidth,
    int ImageTtlMinutes,
    int TimeoutSeconds,
    bool UrlFetch,
    bool ApiKeyRequired);

/// <param name="Name">The value for convert and resize's format parameter.</param>
/// <param name="MaxSide">Largest width or height the format can store; null when only the pixel limit applies.</param>
public sealed record FormatLimits(string Name, string MimeType, string Extension, int? MaxSide);

public sealed record HealthResponse(string Status, string Uptime, string Timestamp, BuildInfo Build);

/// <summary>Fixed when the image is built.</summary>
/// <param name="Date">When the image was built, in the container timezone.</param>
/// <param name="DotNet">The .NET runtime compiled into the binary.</param>
/// <param name="Arch">Docker platform architecture: amd64 or arm64.</param>
public sealed record BuildInfo(
    string Version,
    string Date,
    [property: JsonPropertyName("dotnet")] string DotNet,
    string Arch);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HostedImageResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ImageInfoResponse))]
[JsonSerializable(typeof(LimitsResponse))]
[JsonSerializable(typeof(ProblemDetails))]
internal partial class ImageMasonJsonContext : JsonSerializerContext;
