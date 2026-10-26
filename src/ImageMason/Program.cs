using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using ImageMason;
using ImageMason.Configuration;
using ImageMason.Endpoints;
using ImageMason.Http;
using ImageMason.Imaging;
using ImageMason.Storage;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateSlimBuilder(args);

ImageMasonOptions options;
try
{
    options = ImageMasonOptions.Load(builder.Configuration);
}
catch (InvalidSettingException ex)
{
    Console.Error.WriteLine($"Invalid configuration: {ex.Message}");
    return 1;
}

builder.Logging.AddFilter("ImageMason", options.LogLevel);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = options.MaxInputBytes);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<UrlFetchPolicy>();
builder.Services.AddSingleton<ImageDownloader>();
builder.Services.AddSingleton<ToolRunner>();
var memoryLimit = options.MemoryLimitBytes ?? JobMemoryGate.DetectMemoryLimit();
var jobMemory = new JobMemoryGate(Math.Max(0, memoryLimit - ImageMasonOptions.ServiceReserveBytes));
builder.Services.AddSingleton(jobMemory);
builder.Services.AddSingleton<ImageOptimizer>();
builder.Services.AddSingleton<ImageStore>();
builder.Services.AddHostedService<ImageStoreCleanupService>();
// One log category for every API endpoint, so their messages can be found (and filtered) together.
builder.Services.AddKeyedSingleton(ApiServices.LogCategory,
    (services, _) => services.GetRequiredService<ILoggerFactory>().CreateLogger(ApiServices.LogCategory));

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, ImageMasonJsonContext.Default);
    json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // Responses are never embedded in HTML, so '+' in UTC offsets can stay readable instead of +.
    json.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

builder.Services.AddApiVersioning(versioning =>
{
    versioning.DefaultApiVersion = new ApiVersion(1, 0);
    versioning.ReportApiVersions = true;
    versioning.ApiVersionReader = new UrlSegmentApiVersionReader();
});

// Caps CPU-heavy jobs. Requests beyond the queue get 503, so the caller can retry or keep its original image.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
    limiter.OnRejected = (rejected, _) =>
    {
        rejected.HttpContext.Response.Headers.RetryAfter = ApiRequest.RetryAfterSeconds;
        return ValueTask.CompletedTask;
    };
    limiter.AddConcurrencyLimiter(ApiEndpoints.JobsPolicy, concurrency =>
    {
        concurrency.PermitLimit = options.MaxConcurrentJobs;
        concurrency.QueueLimit = options.MaxQueuedJobs;
        concurrency.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    limiter.AddConcurrencyLimiter(ApiEndpoints.TransfersPolicy, concurrency =>
    {
        concurrency.PermitLimit = options.MaxConcurrentTransfers;
        concurrency.QueueLimit = options.MaxQueuedJobs;
        concurrency.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ImageMason");
logger.LogInformation("Image Mason {Version}", AppInfo.Version);
logger.LogInformation("Memory for optimization jobs: {Budget} of {Limit} ({Source})",
    ByteSizes.ToGigabytes(jobMemory.BudgetBytes), ByteSizes.ToGigabytes(memoryLimit),
    options.MemoryLimitBytes is null ? "detected" : ImageMasonOptions.Prefix + "MEMORY_LIMIT");
if (string.IsNullOrEmpty(options.ApiKey))
    logger.LogWarning("{Setting} is not set; the API accepts unauthenticated requests", ImageMasonOptions.Prefix + "API_KEY");

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseApiKeyCheck(options.ApiKey);
app.UseRateLimiter();

app.MapSite(options);
app.MapHealth();
app.MapApi();

app.Run();
return 0;
