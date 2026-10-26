using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>POST /api/v1/info: format, dimensions and other header details of an image, without decoding it.</summary>
internal static class InfoEndpoint
{
    public static async Task<IResult> HandleAsync([AsParameters] ApiServices api, string? url, string? source)
    {
        using var timeout = api.StartTimeout();

        try
        {
            var input = await ApiRequest.ReadImageAsync(api.Http.Request, api.Downloader, api.Store, url, source, timeout.Token);
            var format = ImageFormatDetector.Detect(input);
            if (format == ImageFormat.Unknown)
                return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, $"Unsupported image format; expected {ImageFormatDetector.SupportedNames}.");

            var info = ImageInfo.Read(input, format);
            if (info.Width <= 0 || info.Height <= 0)
                return ApiRequest.Problem(StatusCodes.Status415UnsupportedMediaType, "Could not read the image dimensions.");

            var details = ImageHeader.ReadDetails(input, format);
            var withinLimit = info.PixelCount <= api.Options.MaxPixels;
            return Results.Ok(new ImageInfoResponse
            {
                Format = format.Name(),
                MimeType = format.ContentType(),
                Size = input.LongLength,
                Width = info.Width,
                Height = info.Height,
                Orientation = info.Orientation,
                DisplayWidth = info.DisplayWidth,
                DisplayHeight = info.DisplayHeight,
                Megapixels = Math.Round(info.PixelCount / 1e6, 2),
                BitDepth = details.BitDepth,
                HasAlpha = details.HasAlpha,
                HasIccProfile = details.HasIccProfile,
                Animated = info.Animated,
                IsPhoto = format == ImageFormat.Png && withinLimit ? PngPhoto.IsPhoto(input, api.Options.MaxPixels, timeout.Token) : null,
                WithinPixelLimit = withinLimit,
                Exif = ExifReader.Read(input, format),
            });
        }
        catch (Exception ex) when (ApiRequest.IsExpected(ex, api.Http))
        {
            return ApiRequest.Problem(api, ex, inputBytes: 0, job: null);
        }
    }
}
