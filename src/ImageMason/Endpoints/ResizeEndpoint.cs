namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/resize?width=800&amp;height=600&amp;fit=cover: resizes to a box, optionally converting the format, and
/// optimizes the result with the same quality presets and parameters as optimize.
/// </summary>
internal static class ResizeEndpoint
{
    public static async Task<IResult> HandleAsync([AsParameters] ApiServices api, [AsParameters] JobParameters job,
        [AsParameters] ResizeParameters box)
    {
        if (box.Width is null && box.Height is null)
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Send width, height or both.");

        if (!box.TryParse(out var resize, out var resizeError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, resizeError!);

        if (!RectParameter.TryParse(job.Rect, out var area, out var rectError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, rectError!);

        if (!ApiRequest.TryParseFormat(job.Format, required: false, out var target, out var formatError))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, formatError!);

        return await OptimizeEndpoint.RunAsync(api, job, target, resize, area, maxWidth: null, preferJpeg: false);
    }
}
