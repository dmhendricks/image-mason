using System.Collections.Frozen;

namespace ImageMason.Endpoints;

/// <summary>
/// Rejects query parameters an endpoint doesn't take, for endpoints marked with <see cref="AllowQuery{TBuilder}"/>,
/// so a misspelled or removed parameter gets 400 instead of being ignored (and the caller getting a different image
/// than they asked for). It runs as middleware before the rate limiter, like <see cref="ApiKeyCheck"/>, so these
/// requests don't take a job slot.
/// </summary>
internal static class QueryCheck
{
    private sealed record AllowedQuery(string[] Names, FrozenSet<string> Set);

    /// <param name="names">Every parameter the endpoint takes, in the order the error message lists them. Matched
    /// ignoring case, as binding does.</param>
    public static TBuilder AllowQuery<TBuilder>(this TBuilder builder, params string[] names) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowedQuery(names, names.ToFrozenSet(StringComparer.OrdinalIgnoreCase)));

    /// <summary>Adds the check. Call it before UseRateLimiter.</summary>
    public static IApplicationBuilder UseQueryCheck(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (http.GetEndpoint()?.Metadata.GetMetadata<AllowedQuery>() is { } allowed)
            {
                var unknown = http.Request.Query.Keys.Where(name => !allowed.Set.Contains(name)).ToArray();
                if (unknown.Length > 0)
                {
                    await ApiRequest.Problem(StatusCodes.Status400BadRequest, Message(unknown, allowed.Names)).ExecuteAsync(http);
                    return;
                }
            }

            await next(http);
        });

    /// <summary>"Unknown parameter: rect. This endpoint takes quality, minScore, …" or "… takes no parameters."</summary>
    internal static string Message(IReadOnlyList<string> unknown, IReadOnlyList<string> allowed) =>
        $"Unknown parameter{(unknown.Count == 1 ? "" : "s")}: {string.Join(", ", unknown)}. "
        + (allowed.Count == 0 ? "This endpoint takes no parameters." : $"This endpoint takes {string.Join(", ", allowed)}.");
}
