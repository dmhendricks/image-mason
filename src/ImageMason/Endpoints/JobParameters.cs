using Microsoft.AspNetCore.Mvc;

namespace ImageMason.Endpoints;

/// <summary>
/// transform's query parameters other than the resize box (<see cref="ResizeParameters"/>), bound with [AsParameters]
/// and checked by <see cref="TransformEndpoint.HandleAsync"/>. Properties rather than a primary constructor: the
/// Request Delegate Generator can't bind a constructor's nullable strings.
/// </summary>
internal sealed record JobParameters
{
    public string? Format { get; init; }
    public string? Quality { get; init; }
    public double? MinScore { get; init; }

    /// <summary>The area to keep: left,top,width,height.</summary>
    public string? Crop { get; init; }

    public string? Url { get; init; }
    public string? Source { get; init; }

    /// <summary>
    /// The store parameter. Named apart from <see cref="ApiServices.Store"/>, which the Request Delegate Generator
    /// would otherwise declare twice. Nullable so it's optional; null means false.
    /// </summary>
    [FromQuery(Name = "store")]
    public bool? StoreResult { get; init; }

    public string? Filename { get; init; }

    /// <summary>Nullable so it's optional, as a bool parameter defaulting to false would be; null means false.</summary>
    public bool? KeepOriginal { get; init; }
}
