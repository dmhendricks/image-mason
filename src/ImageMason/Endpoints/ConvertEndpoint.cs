namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/convert?format=webp: returns the image in another format (JPEG, PNG, WebP, AVIF or JPEG XL), optimized with
/// the same quality presets and parameters as optimize, resizing included.
/// </summary>
internal static class ConvertEndpoint
{
    public static async Task<IResult> HandleAsync([AsParameters] ApiServices api, [AsParameters] JobParameters job,
        [AsParameters] ResizeParameters box)
    {
        if (!ApiRequest.TryParseFormat(job.Format, required: true, out var target, out var formatError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, formatError!);

        if (!RectParameter.TryParse(job.Rect, out var area, out var rectError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, rectError!);

        if (!box.TryParse(out var resize, out var resizeError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, resizeError!);

        return await OptimizeEndpoint.RunAsync(api, job, target, resize, area, preferJpeg: false);
    }
}
