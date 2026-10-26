using System.Security.Cryptography;
using System.Text;

namespace ImageMason.Endpoints;

/// <summary>
/// Checks the X-Api-Key header for endpoints marked with <see cref="RequireApiKey{TBuilder}"/>. It runs as middleware
/// before the rate limiter, so requests without the key get 401 without taking a job slot or a queue place.
/// </summary>
internal static class ApiKeyCheck
{
    /// <summary>Endpoint metadata. The last one added wins, so an endpoint can opt out of its group's requirement.</summary>
    private sealed record ApiKeyMetadata(bool Required);

    private static readonly ApiKeyMetadata Required = new(true);
    private static readonly ApiKeyMetadata NotRequired = new(false);

    public static TBuilder RequireApiKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(Required);

    public static TBuilder AllowWithoutApiKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(NotRequired);

    /// <summary>Adds the check when an API key is set. Call it before UseRateLimiter.</summary>
    public static IApplicationBuilder UseApiKeyCheck(this IApplicationBuilder app, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return app;

        // Hashing first makes the comparison constant-time regardless of key length.
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

        return app.Use(async (http, next) =>
        {
            if (http.GetEndpoint()?.Metadata.GetMetadata<ApiKeyMetadata>() is { Required: true })
            {
                var provided = SHA256.HashData(Encoding.UTF8.GetBytes(http.Request.Headers["X-Api-Key"].ToString()));
                if (!CryptographicOperations.FixedTimeEquals(provided, expected))
                {
                    await ApiRequest.Problem(StatusCodes.Status401Unauthorized, "Send a valid API key in the X-Api-Key header.")
                        .ExecuteAsync(http);
                    return;
                }
            }

            await next(http);
        });
    }
}
