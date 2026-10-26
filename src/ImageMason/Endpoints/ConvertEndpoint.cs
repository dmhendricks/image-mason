namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/convert?format=webp: returns the image in another format (JPEG, PNG, WebP, AVIF or JPEG XL), optimized with
/// the same quality presets and parameters as optimize.
/// </summary>
internal static class ConvertEndpoint
{
    public static async Task<IResult> HandleAsync([AsParameters] ApiServices api, [AsParameters] JobParameters job, int? maxWidth)
    {
        if (!ApiRequest.TryParseFormat(job.Format, required: true, out var target, out var formatError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, formatError!);

        if (!RectParameter.TryParse(job.Rect, out var area, out var rectError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, rectError!);

        return await OptimizeEndpoint.RunAsync(api, job, target, resize: null, area, maxWidth, preferJpeg: false);
    }
}
