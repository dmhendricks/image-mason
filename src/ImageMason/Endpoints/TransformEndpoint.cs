using System.Diagnostics;
using System.Globalization;
using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/transform: optimizes an image, optionally cropping it to crop, resizing it to a width/height box and
/// converting it to format, and returns it with stats in X-Image-Mason-* headers, or with store=true stores it and
/// returns its URL and the same stats.
/// </summary>
internal static class TransformEndpoint
{
    /// <summary>The query parameters transform takes; see <see cref="QueryCheck"/>.</summary>
    public static readonly string[] Parameters =
        ["quality", "minScore", "format", "crop", "width", "height", "fit", "position", "upscale", "url", "source", "store", "filename", "keepOriginal"];

    public static async Task<IResult> HandleAsync(
        [AsParameters] ApiServices api,
        [AsParameters] JobParameters job,
        [AsParameters] ResizeParameters box)
    {
        var (http, options, downloader, optimizer, store, logger) = api;

        if (!CropParameter.TryParse(job.Crop, out var rect, out var cropError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, cropError!);

        if (!box.TryParse(out var resize, out var resizeError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, resizeError!);

        // auto: JPEG for a PNG photo, otherwise the input's format, decided once the image is read.
        var auto = string.Equals(job.Format?.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
        ImageFormat? target = null;
        if (!auto && !ApiRequest.TryParseFormat(job.Format, required: false, out target, out var formatError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, formatError!);

        var preset = options.DefaultQuality;
        if (!string.IsNullOrEmpty(job.Quality) && !QualityPresets.TryParse(job.Quality, out preset))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "quality must be 'lossless', 'high', 'medium' or 'low'.");

        if (job.MinScore is < 0 or > 100)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "minScore must be between 0 and 100.");

        var stored = job.StoreResult is true;
        if (!ApiRequest.TryParseFilename(job.Filename, out var storedName, out var nameError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, nameError!);

        if (storedName is not null && !stored)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "filename is only used with store=true.");

        if (job.KeepOriginal is true && !stored)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "keepOriginal is only used with store=true.");

        using var timeout = api.StartTimeout();

        var stopwatch = Stopwatch.StartNew();
        byte[] input = [];
        try
        {
            input = await ApiRequest.ReadImageAsync(http.Request, downloader, store, job.Url, job.Source, timeout.Token);

            var detected = ImageFormatDetector.Detect(input);
            bool? isPhoto = detected == ImageFormat.Png ? PngPhoto.IsPhoto(input, options.MaxPixels, timeout.Token) : null;
            if (auto && isPhoto == true)
                target = ImageFormat.Jpeg;

            if (storedName is not null)
            {
                var storedFormat = target ?? detected;
                if (storedFormat != ImageFormat.Unknown && !storedName.EndsWith(storedFormat.Extension(), StringComparison.Ordinal))
                    return ApiRequest.Problem(StatusCodes.Status400BadRequest, $"filename must end in {storedFormat.Extension()}.");
            }

            var result = await optimizer.OptimizeAsync(input, new OptimizeRequest(preset, job.MinScore, target, resize, rect, isPhoto), timeout.Token);
            var elapsedMs = (long)stopwatch.Elapsed.TotalMilliseconds;
            var qualityName = preset.ToString().ToLowerInvariant();
            var score = result.Score?.ToString("F2", CultureInfo.InvariantCulture);
            var floor = result.MinScore?.ToString("0.##", CultureInfo.InvariantCulture);
            var before = $"{result.OriginalInfo.Width}x{result.OriginalInfo.Height}";
            var after = $"{result.Info.Width}x{result.Info.Height}";
            var sizeChange = result.OriginalSize == 0
                ? 0
                : Math.Round((result.Data.Length - result.OriginalSize) * 100d / result.OriginalSize, 2);
            var sizeChangeText = sizeChange.ToString("0.##", CultureInfo.InvariantCulture);

            var headers = http.Response.Headers;
            headers["X-Image-Mason-Quality"] = qualityName;
            headers["X-Image-Mason-Original-Size"] = result.OriginalSize.ToString(CultureInfo.InvariantCulture);
            headers["X-Image-Mason-Size"] = result.Data.Length.ToString(CultureInfo.InvariantCulture);
            headers["X-Image-Mason-Size-Change-Percent"] = sizeChangeText;
            headers["X-Image-Mason-Original-Dimensions"] = before;
            headers["X-Image-Mason-Dimensions"] = after;
            headers["X-Image-Mason-Method"] = result.Method;
            headers["X-Image-Mason-Duration-Ms"] = elapsedMs.ToString(CultureInfo.InvariantCulture);
            if (score is not null)
                headers["X-Image-Mason-Score"] = score;
            if (floor is not null)
                headers["X-Image-Mason-Min-Score"] = floor;
            if (isPhoto is { } photo)
                headers["X-Image-Mason-Is-Photo"] = photo ? "true" : "false";

            var formats = detected == result.Format ? result.Format.Name() : $"{detected.Name()} -> {result.Format.Name()}";
            var sizeChangeLog = $"{Math.Abs(sizeChange).ToString("0.##", CultureInfo.InvariantCulture)}% {(sizeChange > 0 ? "larger" : "smaller")}";
            logger.LogInformation("{Quality} {Formats} {Before}: {BeforeBytes} -> {AfterBytes} bytes ({SizeChange}), {After} via {Method} (score {Score}, min {MinScore}, isPhoto {IsPhoto}) in {ElapsedMs} ms",
                preset, formats, before, result.OriginalSize, result.Data.Length, sizeChangeLog, after, result.Method, score ?? "n/a", floor ?? "n/a", isPhoto?.ToString() ?? "n/a", elapsedMs);

            if (!stored)
                return Results.Bytes(result.Data, result.Format.ContentType());

            var origin = ApiRequest.Origin(http.Request, options.PublicBaseUrl);
            string? inputUrl = null;
            if (job.KeepOriginal is true && !string.IsNullOrWhiteSpace(job.Source))
            {
                // Already stored: name the stored copy rather than storing it twice.
                inputUrl = $"{origin}/images/{job.Source.Trim()}";
            }
            else if (job.KeepOriginal is true)
            {
                var savedInput = await store.SaveAsync(input, detected, null, timeout.Token);
                inputUrl = $"{origin}/images/{savedInput.Name}";
                logger.LogInformation("Stored input {Name} ({Bytes} bytes) at {Url}", savedInput.Name, input.Length, inputUrl);
            }

            var saved = await store.SaveAsync(result.Data, result.Format, storedName, timeout.Token);
            var outputUrl = $"{origin}/images/{saved.Name}";
            logger.LogInformation("Stored {Name} ({Bytes} bytes) at {Url}", saved.Name, result.Data.Length, outputUrl);

            return Results.Ok(new HostedImageResponse
            {
                Output = outputUrl,
                Input = inputUrl,
                MimeType = result.Format.ContentType(),
                OriginalSize = result.OriginalSize,
                Size = result.Data.Length,
                SizeChangePercent = sizeChange,
                OriginalWidth = result.OriginalInfo.Width,
                OriginalHeight = result.OriginalInfo.Height,
                Width = result.Info.Width,
                Height = result.Info.Height,
                Quality = qualityName,
                Method = result.Method,
                Score = result.Score is null ? null : Math.Round(result.Score.Value, 2),
                MinScore = result.MinScore,
                IsPhoto = isPhoto,
                ExpiresAt = ApiRequest.LocalTimestamp(saved.ExpiresAtUtc),
                DurationMs = elapsedMs,
            });
        }
        catch (Exception ex) when (ApiRequest.IsExpected(ex, http))
        {
            return ApiRequest.Problem(api, ex, input.Length, "Request");
        }
    }
}
