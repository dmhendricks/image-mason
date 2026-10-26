using System.Diagnostics;
using System.Globalization;
using ImageMason.Configuration;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;

namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/optimize: returns a smaller image in the same format (or in format, when sent), optionally cropped
/// to rect and resized to maxWidth or to a width/height box like resize, with stats in X-Image-Mason-* headers, or
/// with output=json stores it and returns its URL and the same stats.
/// </summary>
internal static class OptimizeEndpoint
{
    /// <summary>Optimize's parameter checks and run. Also the crop endpoint's, after it checks rect.</summary>
    public static async Task<IResult> HandleAsync(
        [AsParameters] ApiServices api,
        [AsParameters] JobParameters job,
        [AsParameters] ResizeParameters box,
        int? maxWidth,
        bool preferJpeg = false)
    {
        if (!RectParameter.TryParse(job.Rect, out var area, out var rectError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, rectError!);

        if (!box.TryParse(out var resize, out var resizeError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, resizeError!);

        if (resize is not null && maxWidth is not null)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Send maxWidth or width/height, not both.");

        // An explicit format wins over preferJpeg.
        if (!ApiRequest.TryParseFormat(job.Format, required: false, out var target, out var formatError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, formatError!);

        return await RunAsync(api, job, target, resize, area, maxWidth, preferJpeg);
    }

    /// <summary>Shared by optimize, convert, resize and crop, after their parameter checks.</summary>
    /// <param name="target">The output format; null keeps the input's (or JPEG for a photo PNG with preferJpeg).</param>
    /// <param name="resize">The resize endpoint's box; null uses maxWidth.</param>
    /// <param name="rect">The area to keep, cut out before resizing; null keeps the whole image.</param>
    public static async Task<IResult> RunAsync(ApiServices api, JobParameters job, ImageFormat? target, ResizeRequest? resize,
        CropRect? rect, int? maxWidth, bool preferJpeg)
    {
        var (http, options, downloader, optimizer, store, logger) = api;

        var preset = options.DefaultQuality;
        if (!string.IsNullOrEmpty(job.Quality) && !QualityPresets.TryParse(job.Quality, out preset))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "quality must be 'lossless', 'high', 'medium' or 'low'.");

        if (job.MinScore is < 0 or > 100)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "minScore must be between 0 and 100.");

        if (maxWidth is < 0)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "maxWidth must be 0 (no resize) or a positive width.");

        bool asJson;
        switch (job.Output?.ToLowerInvariant())
        {
            case null or "":
                asJson = false;
                break;
            case "json":
                asJson = true;
                break;
            default:
                return ApiRequest.Problem(StatusCodes.Status400BadRequest, "output must be 'json'.");
        }

        if (!ApiRequest.TryParseOutputFilename(job.OutputFilename, out var storedName, out var nameError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, nameError!);

        if (storedName is not null && !asJson)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "outputFilename is only used with output=json.");

        if (job.KeepOriginal is true && !asJson)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "keepOriginal is only used with output=json.");

        using var timeout = api.StartTimeout();

        var stopwatch = Stopwatch.StartNew();
        byte[] input = [];
        try
        {
            input = await ApiRequest.ReadImageAsync(http.Request, downloader, store, job.Url, job.Source, timeout.Token);

            var detected = ImageFormatDetector.Detect(input);
            bool? isPhoto = detected == ImageFormat.Png ? PngPhoto.IsPhoto(input, options.MaxPixels, timeout.Token) : null;
            target ??= preferJpeg && isPhoto == true ? ImageFormat.Jpeg : null;

            if (storedName is not null)
            {
                var stored = target ?? detected;
                if (stored != ImageFormat.Unknown && !storedName.EndsWith(stored.Extension(), StringComparison.Ordinal))
                    return ApiRequest.Problem(StatusCodes.Status400BadRequest, $"outputFilename must end in {stored.Extension()}.");
            }

            var result = await optimizer.OptimizeAsync(input, new OptimizeRequest(preset, job.MinScore, maxWidth, target, resize, rect, isPhoto), timeout.Token);
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
            headers["X-Image-Mason-Optimized-Size"] = result.Data.Length.ToString(CultureInfo.InvariantCulture);
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

            if (!asJson)
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
                CompressedSize = result.Data.Length,
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
