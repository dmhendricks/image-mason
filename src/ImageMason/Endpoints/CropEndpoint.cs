namespace ImageMason.Endpoints;

/// <summary>
/// POST /api/v1/crop?rect=80,0,480,480: keeps the given area, optionally resizing or converting it, and optimizes the
/// result. The same parameters as optimize, with rect required.
/// </summary>
internal static class CropEndpoint
{
    public static async Task<IResult> HandleAsync(
        [AsParameters] ApiServices api,
        [AsParameters] JobParameters job,
        [AsParameters] ResizeParameters box,
        int? maxWidth,
        bool preferJpeg = false)
    {
        if (string.IsNullOrWhiteSpace(job.Rect))
            return ApiRequest.Problem(StatusCodes.Status400BadRequest, "Send rect as left,top,width,height, such as 80,0,480,480.");

        return await OptimizeEndpoint.HandleAsync(api, job, box, maxWidth, preferJpeg);
    }
}
