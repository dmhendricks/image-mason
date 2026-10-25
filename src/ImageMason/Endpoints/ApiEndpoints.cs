using Asp.Versioning;

namespace ImageMason.Endpoints;

internal static class ApiEndpoints
{
    /// <summary>Rate-limiter policy for CPU-heavy jobs, configured in Program.cs.</summary>
    public const string JobsPolicy = "jobs";

    /// <summary>Maps the versioned API under /api/v1 (URL segment versioning; /api/v1.0 is the same version).</summary>
    public static void MapApi(this WebApplication app)
    {
        var versions = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        var api = app.MapGroup("/api/v{version:apiVersion}")
            .WithApiVersionSet(versions);

        api.MapPost("/optimize", OptimizeEndpoint.HandleAsync).RequireRateLimiting(JobsPolicy);
        api.MapPost("/convert", ConvertEndpoint.HandleAsync).RequireRateLimiting(JobsPolicy);
        api.MapPost("/resize", ResizeEndpoint.HandleAsync).RequireRateLimiting(JobsPolicy);
        api.MapPost("/crop", CropEndpoint.HandleAsync).RequireRateLimiting(JobsPolicy);
        api.MapPost("/upload", UploadEndpoint.HandleAsync).RequireRateLimiting(JobsPolicy);

        // Reads the header only: no tools run, so no job slot.
        api.MapPost("/info", InfoEndpoint.HandleAsync);

        // Public: reports apiKeyRequired, so it can't require the key itself.
        api.MapGet("/limits", LimitsEndpoint.Handle);
    }
}
