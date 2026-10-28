using Asp.Versioning;

namespace ImageMason.Endpoints;

internal static class ApiEndpoints
{
    /// <summary>Rate-limiter policy for CPU-heavy jobs, configured in Program.cs.</summary>
    public const string JobsPolicy = "jobs";

    /// <summary>Rate-limiter policy for requests that only move an image (no tools), so they hold one in memory at most
    /// MaxConcurrentTransfers times; configured in Program.cs.</summary>
    public const string TransfersPolicy = "transfers";

    /// <summary>Maps the versioned API under /api/v1 (URL segment versioning; /api/v1.0 is the same version).</summary>
    public static void MapApi(this WebApplication app)
    {
        var versions = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        var api = app.MapGroup("/api/v{version:apiVersion}")
            .WithApiVersionSet(versions)
            .RequireApiKey();

        api.MapPost("/transform", TransformEndpoint.HandleAsync).AllowQuery(TransformEndpoint.Parameters).RequireRateLimiting(JobsPolicy);
        // No tools run, so these don't wait behind jobs; each still holds the image in memory, so they have their own limit.
        api.MapPost("/upload", UploadEndpoint.HandleAsync).AllowQuery(UploadEndpoint.Parameters).RequireRateLimiting(TransfersPolicy);
        api.MapPost("/info", InfoEndpoint.HandleAsync).AllowQuery("url", "source").RequireRateLimiting(TransfersPolicy);
        api.MapPost("/fetch", FetchEndpoint.HandleAsync).AllowQuery("url").RequireRateLimiting(TransfersPolicy);

        // Public: reports apiKeyRequired, so it can't require the key itself.
        api.MapGet("/limits", LimitsEndpoint.Handle).AllowQuery().AllowWithoutApiKey();
    }
}
